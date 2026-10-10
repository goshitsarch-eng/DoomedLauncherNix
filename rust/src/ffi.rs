use crate::{api, error::Error};
use std::{
    ffi::{CString, c_char},
    panic::{AssertUnwindSafe, catch_unwind},
};
#[unsafe(no_mangle)]
pub extern "C" fn dl_abi_version() -> u32 {
    1
}
/// Invoke a typed domain operation from a borrowed UTF-8 buffer.
/// # Safety
/// `input` must point to `length` readable bytes for this call. The returned
/// allocation is owned by Rust and must be released exactly once with dl_free.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn dl_invoke(input: *const u8, length: usize) -> *mut c_char {
    let result = catch_unwind(AssertUnwindSafe(|| {
        if input.is_null() || length == 0 || length > 8 * 1024 * 1024 {
            return Err(Error::validation("Invalid request buffer length."));
        }
        // SAFETY: the calling bridge supplies this valid borrowed byte range.
        let bytes = unsafe { std::slice::from_raw_parts(input, length) };
        let request: api::Request = serde_json::from_slice(bytes)?;
        api::dispatch(request)
    }));
    let value = match result {
        Ok(Ok(value)) => serde_json::json!({"ok":true,"result":value}),
        Ok(Err(error)) => serde_json::json!({"ok":false,"error":error}),
        Err(_) => {
            serde_json::json!({"ok":false,"error":{"code":"internal","message":"The application core encountered an unexpected error. Your library remains on disk; restart and retry the operation."}})
        }
    };
    match CString::new(value.to_string()) {
        Ok(value) => value.into_raw(),
        Err(_) => std::ptr::null_mut(),
    }
}
/// # Safety
/// `value` must be null or a live allocation returned by dl_invoke, and must
/// never be used or freed again after this call.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn dl_free(value: *mut c_char) {
    if !value.is_null() {
        // SAFETY: ownership is returned by our documented caller contract.
        unsafe {
            drop(CString::from_raw(value));
        }
    }
}
