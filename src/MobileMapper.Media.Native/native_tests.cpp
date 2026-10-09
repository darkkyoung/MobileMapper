#include <cstring>
#include <iostream>
extern "C" __declspec(dllimport) unsigned mm_abi_version() noexcept;
extern "C" __declspec(dllimport) const char* mm_codec_license() noexcept;
int main() {
    if (mm_abi_version() != 1 || !std::strstr(mm_codec_license(), "LGPL")) return 1;
    std::cout << "PASS ABI and runtime LGPL license\n";
    return 0;
}
