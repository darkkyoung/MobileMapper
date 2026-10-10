#include "media.h"
#include "test_frame.h"
#include <windows.h>
#include <cstring>
#include <iostream>
#include <thread>
#include <chrono>
int main() {
    int checks = 0;
    auto require = [&](bool value, const char* name) {
        if (!value) { std::cerr << "FAIL " << name << '\n'; return false; }
        ++checks; return true;
    };
    const char* configuration = mm_codec_configuration();
    if (!require(mm_abi_version() == 1, "ABI") ||
        !require(std::strstr(mm_codec_license(), "LGPL") != nullptr, "LGPL runtime license") ||
        !require(!std::strstr(configuration, "--enable-gpl") && !std::strstr(configuration, "--enable-nonfree") &&
                 !std::strstr(configuration, "--enable-version3"), "no GPL/nonfree/version3")) return 1;
    std::cout << "FFmpeg configuration: " << configuration << '\n';
    if (!require(mm_create(nullptr, 1) == -1, "null ABI output")) return 1;
    void* handle = nullptr;
    if (!require(mm_create(&handle, 1) == 0 && handle, "WARP D3D11 composition swapchain create")) return 1;
    struct Cleanup { void* p; ~Cleanup() { mm_destroy(p); } } cleanup{handle};
    if (!require(mm_resize(handle, 0, 100, 1, 1) == -1, "invalid resize") ||
        !require(mm_resize(handle, 320, 240, 1.5f, 1.5f) == 0, "resize request") ||
        !require(mm_reset(handle, 0) == -1, "invalid generation") ||
        !require(mm_reset(handle, 7) == 0, "decoder reset") ||
        !require(mm_decode(handle, nullptr, 0, 0, 0) == -1, "invalid packet") ||
        !require(mm_decode(handle, test_frame, sizeof(test_frame), 0, 0) == 0, "synthetic H264 decode")) return 1;
    mm_stats stats{};
    if (!require(mm_get_stats(handle, &stats) == 0 && stats.decoded == 1, "one decoded frame")) return 1;
    void* chain = nullptr;
    if (!require(mm_get_swapchain(handle, &chain) == 0 && chain, "owned COM swapchain reference")) return 1;
    static_cast<IUnknown*>(chain)->Release();
    if (!require(mm_reset(handle, 8) == 0, "restart decoder generation")) return 1;
    std::cout << "PASS " << checks << " native checks (headless WARP; no WinUI/device acceptance)\n";
    return 0;
}
