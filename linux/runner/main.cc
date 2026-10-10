#include "my_application.h"
#include "../../packaging/native/cli.h"

int main(int argc, char** argv) {
  if (HandleDoomLauncherCli(std::vector<std::string>(argv + 1, argv + argc))) {
    return 0;
  }
  g_autoptr(MyApplication) app = my_application_new();
  return g_application_run(G_APPLICATION(app), argc, argv);
}
