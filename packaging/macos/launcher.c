#include <libgen.h>
#include <limits.h>
#include <mach-o/dyld.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

int main(int argc, char **argv)
{
    (void)argc;

    char executable_path[PATH_MAX];
    uint32_t size = sizeof(executable_path);
    if (_NSGetExecutablePath(executable_path, &size) != 0)
    {
        fputs("ReelPress launcher path is too long.\n", stderr);
        return 1;
    }

    char resolved_path[PATH_MAX];
    if (realpath(executable_path, resolved_path) == NULL)
    {
        perror("Unable to resolve ReelPress launcher path");
        return 1;
    }

    char directory_buffer[PATH_MAX];
    if (snprintf(directory_buffer, sizeof(directory_buffer), "%s", resolved_path) >= (int)sizeof(directory_buffer))
    {
        fputs("ReelPress launcher directory is too long.\n", stderr);
        return 1;
    }

    const char *architecture;
#if defined(__arm64__)
    architecture = "osx-arm64";
#elif defined(__x86_64__)
    architecture = "osx-x64";
#else
#error Unsupported macOS architecture
#endif

    char target_path[PATH_MAX];
    if (snprintf(target_path, sizeof(target_path), "%s/%s/ReelPress.Desktop", dirname(directory_buffer), architecture)
        >= (int)sizeof(target_path))
    {
        fputs("ReelPress payload path is too long.\n", stderr);
        return 1;
    }

    argv[0] = target_path;
    execv(target_path, argv);
    perror("Unable to start ReelPress payload");
    return 1;
}
