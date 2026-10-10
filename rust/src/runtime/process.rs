//! OS-owned process trees. Child wrappers must not outlive their game session.

use std::{
    io,
    process::{Child, Command, ExitStatus},
    thread,
    time::Duration,
};

pub(super) struct OwnedProcess {
    child: Child,
    finished: bool,
    #[cfg(unix)]
    group: libc::pid_t,
    #[cfg(windows)]
    job: std::os::windows::io::OwnedHandle,
}

impl OwnedProcess {
    pub(super) fn spawn(command: &mut Command) -> io::Result<Self> {
        #[cfg(unix)]
        {
            use std::os::unix::process::CommandExt;
            command.process_group(0);
        }
        #[cfg(windows)]
        {
            use std::os::windows::process::CommandExt;
            // Assign the job before any engine code can spawn descendants.
            command.creation_flags(windows_sys::Win32::System::Threading::CREATE_SUSPENDED);
        }
        #[cfg(windows)]
        let job = windows::create_job()?;
        let mut child = command.spawn()?;
        #[cfg(windows)]
        if let Err(error) = windows::assign_and_resume(&job, &child) {
            let _ = child.kill();
            let _ = child.wait();
            return Err(error);
        }
        // On Unix the creation path above establishes pgid = child pid.
        #[cfg(unix)]
        let group = child.id() as libc::pid_t;
        // `mut` is needed only for the Windows assignment failure cleanup.
        #[cfg(not(windows))]
        let _ = &mut child;
        Ok(Self {
            child,
            finished: false,
            #[cfg(unix)]
            group,
            #[cfg(windows)]
            job,
        })
    }

    pub(super) fn id(&self) -> u32 {
        self.child.id()
    }

    pub(super) fn try_wait(&mut self) -> io::Result<Option<ExitStatus>> {
        let Some(status) = self.child.try_wait()? else {
            return Ok(None);
        };
        if self.tree_active()? {
            return Ok(None);
        }
        self.finished = true;
        Ok(Some(status))
    }

    pub(super) fn kill(&mut self) -> io::Result<()> {
        if self.finished {
            return Ok(());
        }
        #[cfg(unix)]
        {
            // SAFETY: this strictly positive pgid was created for our child;
            // the negative argument targets only that owned process group.
            if unsafe { libc::kill(-self.group, libc::SIGKILL) } != 0 {
                let error = io::Error::last_os_error();
                if error.raw_os_error() != Some(libc::ESRCH) {
                    return Err(error);
                }
            }
            Ok(())
        }
        #[cfg(windows)]
        {
            windows::terminate(&self.job)
        }
        #[cfg(not(any(unix, windows)))]
        {
            self.child.kill()
        }
    }

    pub(super) fn wait(&mut self) -> io::Result<ExitStatus> {
        loop {
            if let Some(status) = self.try_wait()? {
                return Ok(status);
            }
            thread::sleep(Duration::from_millis(10));
        }
    }

    fn tree_active(&self) -> io::Result<bool> {
        #[cfg(unix)]
        {
            // SAFETY: signal zero only checks the existence of our own group.
            if unsafe { libc::kill(-self.group, 0) } != 0 {
                let error = io::Error::last_os_error();
                return if error.raw_os_error() == Some(libc::ESRCH) {
                    Ok(false)
                } else {
                    Err(error)
                };
            }
            #[cfg(target_os = "linux")]
            {
                // An exited wrapper's descendants are adopted by init. In
                // containers init may leave zombies unreaped indefinitely;
                // zombies own no live game work and must not block shutdown.
                if let Ok(entries) = std::fs::read_dir("/proc") {
                    for entry in entries.flatten() {
                        if entry
                            .file_name()
                            .to_str()
                            .is_none_or(|name| !name.bytes().all(|b| b.is_ascii_digit()))
                        {
                            continue;
                        }
                        let Ok(stat) = std::fs::read_to_string(entry.path().join("stat")) else {
                            continue;
                        };
                        let Some((_, fields)) = stat.rsplit_once(')') else {
                            continue;
                        };
                        let mut fields = fields.split_whitespace();
                        let state = fields.next().unwrap_or("");
                        let _parent = fields.next();
                        let group = fields.next().and_then(|s| s.parse::<libc::pid_t>().ok());
                        if group == Some(self.group) && state != "Z" && state != "X" {
                            return Ok(true);
                        }
                    }
                    return Ok(false);
                }
            }
            Ok(true)
        }
        #[cfg(windows)]
        {
            windows::active_processes(&self.job).map(|count| count > 0)
        }
        #[cfg(not(any(unix, windows)))]
        {
            Ok(false)
        }
    }
}

impl Drop for OwnedProcess {
    fn drop(&mut self) {
        if !self.finished {
            let _ = self.kill();
            let _ = self.child.wait();
        }
    }
}

#[cfg(windows)]
mod windows {
    use std::{
        io,
        mem::{size_of, zeroed},
        os::windows::io::{AsRawHandle, FromRawHandle, OwnedHandle},
        process::Child,
        ptr::{null, null_mut},
    };
    use windows_sys::Win32::{
        Foundation::{HANDLE, INVALID_HANDLE_VALUE},
        System::{
            Diagnostics::ToolHelp::{
                CreateToolhelp32Snapshot, TH32CS_SNAPTHREAD, THREADENTRY32, Thread32First,
                Thread32Next,
            },
            JobObjects::{
                AssignProcessToJobObject, CreateJobObjectW, JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
                JOBOBJECT_BASIC_ACCOUNTING_INFORMATION, JOBOBJECT_EXTENDED_LIMIT_INFORMATION,
                JobObjectBasicAccountingInformation, JobObjectExtendedLimitInformation,
                QueryInformationJobObject, SetInformationJobObject, TerminateJobObject,
            },
            Threading::{OpenThread, ResumeThread, THREAD_SUSPEND_RESUME},
        },
    };

    pub(super) fn create_job() -> io::Result<OwnedHandle> {
        // SAFETY: null security/name select a private unnamed noninheritable
        // kernel job; the returned handle is owned and always closed by RAII.
        let raw = unsafe { CreateJobObjectW(null(), null()) };
        if raw.is_null() {
            return Err(io::Error::last_os_error());
        }
        // SAFETY: raw is a newly created valid owned job handle.
        let handle = unsafe { OwnedHandle::from_raw_handle(raw) };
        // SAFETY: zero is the documented initial state for this POD structure.
        let mut limits: JOBOBJECT_EXTENDED_LIMIT_INFORMATION = unsafe { zeroed() };
        limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        // SAFETY: limits points to an initialized structure of the exact size
        // required by JobObjectExtendedLimitInformation, for a valid job.
        if unsafe {
            SetInformationJobObject(
                handle.as_raw_handle() as HANDLE,
                JobObjectExtendedLimitInformation,
                (&limits as *const JOBOBJECT_EXTENDED_LIMIT_INFORMATION).cast(),
                size_of::<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>() as u32,
            )
        } == 0
        {
            return Err(io::Error::last_os_error());
        }
        Ok(handle)
    }

    pub(super) fn assign_and_resume(job: &OwnedHandle, child: &Child) -> io::Result<()> {
        // SAFETY: both handles are live and the suspended child is ours. No
        // breakaway flag is granted, so its descendants inherit this job.
        if unsafe {
            AssignProcessToJobObject(
                job.as_raw_handle() as HANDLE,
                child.as_raw_handle() as HANDLE,
            )
        } == 0
        {
            return Err(io::Error::last_os_error());
        }
        // std::process::Child does not expose the initial thread handle. A
        // suspended fresh process has exactly one thread; ToolHelp provides
        // the documented way to locate and resume that owned thread.
        // SAFETY: these arguments request a read-only snapshot of threads.
        let raw = unsafe { CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0) };
        if raw == INVALID_HANDLE_VALUE {
            return Err(io::Error::last_os_error());
        }
        // SAFETY: raw is the successfully created owned snapshot handle.
        let snapshot = unsafe { OwnedHandle::from_raw_handle(raw) };
        // SAFETY: THREADENTRY32 is a POD structure initialized with its size.
        let mut entry: THREADENTRY32 = unsafe { zeroed() };
        entry.dwSize = size_of::<THREADENTRY32>() as u32;
        // SAFETY: entry is writable and its dwSize field is initialized.
        let mut found =
            unsafe { Thread32First(snapshot.as_raw_handle() as HANDLE, &mut entry) } != 0;
        while found {
            if entry.th32OwnerProcessID == child.id() {
                // SAFETY: the thread ID comes from our suspended child; only
                // the right required for resuming it is requested.
                let raw_thread =
                    unsafe { OpenThread(THREAD_SUSPEND_RESUME, 0, entry.th32ThreadID) };
                if raw_thread.is_null() {
                    return Err(io::Error::last_os_error());
                }
                // SAFETY: OpenThread returned a new owned live thread handle.
                let thread = unsafe { OwnedHandle::from_raw_handle(raw_thread) };
                // SAFETY: the thread belongs to the successfully assigned job.
                if unsafe { ResumeThread(thread.as_raw_handle() as HANDLE) } == u32::MAX {
                    return Err(io::Error::last_os_error());
                }
                return Ok(());
            }
            // SAFETY: the snapshot and entry remain valid throughout iteration.
            found = unsafe { Thread32Next(snapshot.as_raw_handle() as HANDLE, &mut entry) } != 0;
        }
        Err(io::Error::other(
            "Could not find the suspended source port's initial thread",
        ))
    }

    pub(super) fn terminate(job: &OwnedHandle) -> io::Result<()> {
        // SAFETY: only the private owned job and its descendants are terminated.
        if unsafe { TerminateJobObject(job.as_raw_handle() as HANDLE, 1) } == 0 {
            return Err(io::Error::last_os_error());
        }
        Ok(())
    }

    pub(super) fn active_processes(job: &OwnedHandle) -> io::Result<u32> {
        // SAFETY: the query initializes a correctly sized POD output buffer.
        let mut info: JOBOBJECT_BASIC_ACCOUNTING_INFORMATION = unsafe { zeroed() };
        // SAFETY: the job handle and writable output buffer are live and valid.
        if unsafe {
            QueryInformationJobObject(
                job.as_raw_handle() as HANDLE,
                JobObjectBasicAccountingInformation,
                (&mut info as *mut JOBOBJECT_BASIC_ACCOUNTING_INFORMATION).cast(),
                size_of::<JOBOBJECT_BASIC_ACCOUNTING_INFORMATION>() as u32,
                null_mut(),
            )
        } == 0
        {
            return Err(io::Error::last_os_error());
        }
        Ok(info.ActiveProcesses)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::Instant;

    fn wait_for_completion(process: &mut OwnedProcess) -> ExitStatus {
        let deadline = Instant::now() + Duration::from_secs(5);
        loop {
            if let Some(status) = process.try_wait().unwrap() {
                return status;
            }
            assert!(
                Instant::now() < deadline,
                "owned process tree failed to stop"
            );
            thread::sleep(Duration::from_millis(10));
        }
    }

    #[cfg(unix)]
    #[test]
    fn stopping_owned_group_kills_waiting_wrapper_and_descendant() {
        let temporary = tempfile::tempdir().unwrap();
        let marker = temporary.path().join("child-pid");
        let mut process = OwnedProcess::spawn(Command::new("/bin/sh").args([
            std::ffi::OsStr::new("-c"),
            std::ffi::OsStr::new("sleep 20 & printf '%s' \"$!\" > \"$1\"; wait"),
            std::ffi::OsStr::new("fixture"),
            marker.as_os_str(),
        ]))
        .unwrap();
        let deadline = Instant::now() + Duration::from_secs(5);
        while std::fs::read_to_string(&marker)
            .unwrap_or_default()
            .is_empty()
        {
            assert!(
                Instant::now() < deadline,
                "wrapper failed to spawn its child"
            );
            thread::sleep(Duration::from_millis(5));
        }
        assert!(process.try_wait().unwrap().is_none());
        process.kill().unwrap();
        assert!(!wait_for_completion(&mut process).success());
        assert!(!process.tree_active().unwrap());
    }

    #[cfg(unix)]
    #[test]
    fn child_outliving_wrapper_keeps_session_active_until_it_exits() {
        let mut process =
            OwnedProcess::spawn(Command::new("/bin/sh").args(["-c", "sleep 1 & exit 0"])).unwrap();
        let deadline = Instant::now() + Duration::from_secs(2);
        loop {
            if process.child.try_wait().unwrap().is_some() {
                break;
            }
            assert!(Instant::now() < deadline);
            thread::sleep(Duration::from_millis(5));
        }
        assert!(
            process.try_wait().unwrap().is_none(),
            "wrapper exit must not hide its running engine"
        );
        assert!(wait_for_completion(&mut process).success());
    }

    #[cfg(windows)]
    #[test]
    fn windows_job_contains_and_terminates_descendants() {
        let mut process =
            OwnedProcess::spawn(Command::new("cmd.exe").args(["/C", "ping -n 30 127.0.0.1 > NUL"]))
                .unwrap();
        let deadline = Instant::now() + Duration::from_secs(5);
        while windows::active_processes(&process.job).unwrap() < 2 {
            assert!(
                Instant::now() < deadline,
                "cmd did not spawn its owned ping child"
            );
            thread::sleep(Duration::from_millis(10));
        }
        process.kill().unwrap();
        assert!(!wait_for_completion(&mut process).success());
        assert_eq!(windows::active_processes(&process.job).unwrap(), 0);
    }
}
