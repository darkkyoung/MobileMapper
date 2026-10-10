#include "media.h"
#include <windows.h>
#include <d3d11.h>
#include <dxgi1_3.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <atomic>
#include <algorithm>
#include <cmath>
#include <cstring>
#include <condition_variable>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <thread>
#include <vector>
extern "C" {
#include <libavcodec/avcodec.h>
#include <libswscale/swscale.h>
}
using Microsoft::WRL::ComPtr;
namespace {
void check(HRESULT hr) { if (FAILED(hr)) throw std::runtime_error("D3D operation failed"); }
struct FrameDelete { void operator()(AVFrame* f) const { av_frame_free(&f); } };
using Frame = std::unique_ptr<AVFrame, FrameDelete>;
class Media {
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    ComPtr<IDXGISwapChain2> chain;
    ComPtr<ID3D11VertexShader> vs;
    ComPtr<ID3D11PixelShader> ps;
    ComPtr<ID3D11SamplerState> sampler;
    ComPtr<ID3D11Texture2D> texture;
    ComPtr<ID3D11ShaderResourceView> srv;
    ComPtr<ID3D11RenderTargetView> target;
    int texture_w = 0, texture_h = 0;
    AVCodecContext* decoder = nullptr;
    SwsContext* scaler = nullptr;
    std::vector<uint8_t> config_bytes, bgra;
    std::mutex decode_mutex, queue_mutex;
    std::condition_variable wake;
    Frame latest;
    uint64_t latest_generation = 0;
    bool stopping = false, resized = true;
    int panel_w = 1, panel_h = 1;
    float scale_x = 1, scale_y = 1;
    std::thread renderer;
    std::atomic<uint64_t> generation{0}, decoded{0}, presented{0}, replaced{0}, visible_generation{0};
    std::atomic<int> visible_w{0}, visible_h{0}, error{0};
    void receive() {
        for (;;) {
            Frame frame(av_frame_alloc());
            if (!frame) throw std::bad_alloc();
            int result = avcodec_receive_frame(decoder, frame.get());
            if (result == AVERROR(EAGAIN) || result == AVERROR_EOF) return;
            if (result < 0 || frame->width < 1 || frame->height < 1 || frame->width > 8192 || frame->height > 8192)
                throw std::runtime_error("Invalid decoded frame");
            ++decoded;
            std::lock_guard guard(queue_mutex);
            if (latest) ++replaced;
            latest = std::move(frame); latest_generation = generation.load(); wake.notify_one();
        }
    }
    void resize_target(int w, int h, float sx, float sy) {
        context->OMSetRenderTargets(0, nullptr, nullptr); target.Reset();
        check(chain->ResizeBuffers(0, w, h, DXGI_FORMAT_UNKNOWN, 0));
        DXGI_MATRIX_3X2_F matrix{1 / sx, 0, 0, 1 / sy, 0, 0};
        check(chain->SetMatrixTransform(&matrix));
        ComPtr<ID3D11Texture2D> buffer;
        check(chain->GetBuffer(0, IID_PPV_ARGS(&buffer)));
        check(device->CreateRenderTargetView(buffer.Get(), nullptr, &target));
    }
    void draw(AVFrame* f, int w, int h) {
        if (texture_w != f->width || texture_h != f->height) {
            texture.Reset(); srv.Reset();
            D3D11_TEXTURE2D_DESC desc{};
            desc.Width = f->width; desc.Height = f->height; desc.MipLevels = desc.ArraySize = 1;
            desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM; desc.SampleDesc.Count = 1;
            desc.Usage = D3D11_USAGE_DEFAULT; desc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
            check(device->CreateTexture2D(&desc, nullptr, &texture));
            check(device->CreateShaderResourceView(texture.Get(), nullptr, &srv));
            texture_w = f->width; texture_h = f->height;
        }
        scaler = sws_getCachedContext(scaler, f->width, f->height, (AVPixelFormat)f->format,
            f->width, f->height, AV_PIX_FMT_BGRA, SWS_BILINEAR, nullptr, nullptr, nullptr);
        if (!scaler) throw std::runtime_error("Color converter unavailable");
        auto coefficients = sws_getCoefficients(f->colorspace == AVCOL_SPC_BT709 ? SWS_CS_ITU709 : SWS_CS_DEFAULT);
        sws_setColorspaceDetails(scaler, coefficients, f->color_range == AVCOL_RANGE_JPEG,
            coefficients, 1, 0, 1 << 16, 1 << 16);
        bgra.resize((size_t)f->width * f->height * 4);
        uint8_t* destination[] = {bgra.data()}; int pitch[] = {f->width * 4};
        if (sws_scale(scaler, f->data, f->linesize, 0, f->height, destination, pitch) <= 0)
            throw std::runtime_error("Color conversion failed");
        context->UpdateSubresource(texture.Get(), 0, nullptr, bgra.data(), pitch[0], 0);
        const float black[4] = {0, 0, 0, 1};
        context->ClearRenderTargetView(target.Get(), black);
        float fit = std::min((float)w / f->width, (float)h / f->height);
        D3D11_VIEWPORT viewport{(w - f->width * fit) / 2, (h - f->height * fit) / 2,
            f->width * fit, f->height * fit, 0, 1};
        context->RSSetViewports(1, &viewport);
        context->OMSetRenderTargets(1, target.GetAddressOf(), nullptr);
        context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context->VSSetShader(vs.Get(), nullptr, 0); context->PSSetShader(ps.Get(), nullptr, 0);
        context->PSSetSamplers(0, 1, sampler.GetAddressOf());
        context->PSSetShaderResources(0, 1, srv.GetAddressOf());
        context->Draw(3, 0);
        check(chain->Present(1, 0)); ++presented;
    }
    void render() noexcept {
        HRESULT apartment = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        try {
            Frame current; uint64_t current_generation = 0;
            for (;;) {
                int w, h; float sx, sy; bool resize;
                {
                    std::unique_lock lock(queue_mutex);
                    wake.wait(lock, [&] { return stopping || latest || resized; });
                    if (stopping) break;
                    if (latest) { current = std::move(latest); current_generation = latest_generation; }
                    w = panel_w; h = panel_h; sx = scale_x; sy = scale_y; resize = resized; resized = false;
                }
                if (resize) resize_target(w, h, sx, sy);
                if (current && current_generation == generation.load()) {
                    draw(current.get(), w, h);
                    visible_w = current->width; visible_h = current->height;
                    visible_generation = current_generation;
                }
            }
        } catch (...) { error = -2; }
        if (SUCCEEDED(apartment)) CoUninitialize();
    }
public:
    explicit Media(bool warp) {
        D3D_FEATURE_LEVEL level;
        check(D3D11CreateDevice(nullptr, warp ? D3D_DRIVER_TYPE_WARP : D3D_DRIVER_TYPE_HARDWARE,
            nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0, D3D11_SDK_VERSION, &device, &level, &context));
        ComPtr<IDXGIDevice> dxgi; check(device.As(&dxgi));
        ComPtr<IDXGIAdapter> adapter; check(dxgi->GetAdapter(&adapter));
        ComPtr<IDXGIFactory2> factory; check(adapter->GetParent(IID_PPV_ARGS(&factory)));
        DXGI_SWAP_CHAIN_DESC1 desc{};
        desc.Width = desc.Height = 1; desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        desc.SampleDesc.Count = 1; desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
        desc.BufferCount = 2; desc.Scaling = DXGI_SCALING_STRETCH;
        desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL; desc.AlphaMode = DXGI_ALPHA_MODE_IGNORE;
        ComPtr<IDXGISwapChain1> first;
        check(factory->CreateSwapChainForComposition(device.Get(), &desc, nullptr, &first));
        check(first.As(&chain));
        ComPtr<IDXGIDevice1> latency; check(device.As(&latency)); check(latency->SetMaximumFrameLatency(1));
        const char* source = R"(
struct V { float4 position : SV_Position; float2 uv : TEXCOORD; };
V vertex(uint id : SV_VertexID) { V o; o.uv = float2((id << 1) & 2, id & 2);
 o.position = float4(o.uv * float2(2,-2) + float2(-1,1),0,1); return o; }
Texture2D picture : register(t0); SamplerState linearSampler : register(s0);
float4 pixel(V i) : SV_Target { return picture.Sample(linearSampler, i.uv); }
)";
        ComPtr<ID3DBlob> vertex, pixel, messages;
        check(D3DCompile(source, strlen(source), nullptr, nullptr, nullptr, "vertex", "vs_4_0", 0, 0, &vertex, &messages));
        check(D3DCompile(source, strlen(source), nullptr, nullptr, nullptr, "pixel", "ps_4_0", 0, 0, &pixel, &messages));
        check(device->CreateVertexShader(vertex->GetBufferPointer(), vertex->GetBufferSize(), nullptr, &vs));
        check(device->CreatePixelShader(pixel->GetBufferPointer(), pixel->GetBufferSize(), nullptr, &ps));
        D3D11_SAMPLER_DESC sample{}; sample.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
        sample.AddressU = sample.AddressV = sample.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
        sample.MaxLOD = D3D11_FLOAT32_MAX;
        check(device->CreateSamplerState(&sample, &sampler));
        renderer = std::thread([this] { render(); });
    }
    ~Media() {
        { std::lock_guard guard(queue_mutex); stopping = true; wake.notify_one(); }
        if (renderer.joinable()) renderer.join();
        avcodec_free_context(&decoder); sws_freeContext(scaler);
    }
    void* swapchain() { chain->AddRef(); return chain.Get(); }
    void resize(int w, int h, float sx, float sy) {
        std::lock_guard guard(queue_mutex); panel_w = w; panel_h = h; scale_x = sx; scale_y = sy;
        resized = true; wake.notify_one();
    }
    void reset(uint64_t value) {
        std::lock_guard decode_guard(decode_mutex); avcodec_free_context(&decoder); config_bytes.clear();
        generation = value; visible_generation = 0;
        std::lock_guard guard(queue_mutex); latest.reset();
    }
    void decode(const uint8_t* data, int length, int64_t pts, bool config) {
        std::lock_guard guard(decode_mutex);
        if (!generation.load() || error.load()) throw std::runtime_error("Media not ready");
        if (config) { config_bytes.assign(data, data + length); return; }
        if (!decoder) {
            decoder = avcodec_alloc_context3(avcodec_find_decoder(AV_CODEC_ID_H264));
            if (!decoder) throw std::bad_alloc();
            decoder->thread_count = 2; decoder->thread_type = FF_THREAD_SLICE;
            decoder->flags |= AV_CODEC_FLAG_LOW_DELAY;
            if (avcodec_open2(decoder, decoder->codec, nullptr) < 0) throw std::runtime_error("Decoder open failed");
        }
        auto size = config_bytes.size() + (size_t)length;
        if (size > 16 * 1024 * 1024) throw std::runtime_error("Packet too large");
        AVPacket* raw = av_packet_alloc();
        if (!raw) throw std::bad_alloc();
        std::unique_ptr<AVPacket, void(*)(AVPacket*)> packet(raw, [](AVPacket* p) { av_packet_free(&p); });
        if (av_new_packet(raw, (int)size) < 0) throw std::bad_alloc();
        memcpy(raw->data, config_bytes.data(), config_bytes.size());
        memcpy(raw->data + config_bytes.size(), data, length); config_bytes.clear(); raw->pts = pts;
        int result = avcodec_send_packet(decoder, raw);
        if (result == AVERROR(EAGAIN)) { receive(); result = avcodec_send_packet(decoder, raw); }
        if (result < 0) throw std::runtime_error("H264 decode failed");
        receive();
    }
    mm_stats stats() const { return {decoded.load(), presented.load(), replaced.load(), visible_generation.load(),
        visible_w.load(), visible_h.load(), error.load()}; }
};
}
unsigned mm_abi_version() noexcept { return 1; }
const char* mm_codec_license() noexcept { return avcodec_license(); }
const char* mm_codec_configuration() noexcept { return avcodec_configuration(); }
int mm_create(void** out, int warp) noexcept {
    if (!out) return -1; *out = nullptr;
    try { *out = new Media(warp != 0); return 0; } catch (...) { return -2; }
}
int mm_get_swapchain(void* h, void** out) noexcept {
    if (!h || !out) return -1; *out = static_cast<Media*>(h)->swapchain(); return 0;
}
int mm_resize(void* h, int w, int height, float sx, float sy) noexcept {
    if (!h || w < 1 || height < 1 || w > 16384 || height > 16384 || !std::isfinite(sx) || !std::isfinite(sy) || sx <= 0 || sy <= 0) return -1;
    try { static_cast<Media*>(h)->resize(w, height, sx, sy); return 0; } catch (...) { return -2; }
}
int mm_reset(void* h, uint64_t generation) noexcept {
    if (!h || !generation) return -1;
    try { static_cast<Media*>(h)->reset(generation); return 0; } catch (...) { return -2; }
}
int mm_decode(void* h, const uint8_t* data, int length, int64_t pts, int config) noexcept {
    if (!h || !data || length < 1 || length > 16 * 1024 * 1024) return -1;
    try { static_cast<Media*>(h)->decode(data, length, pts, config != 0); return 0; } catch (...) { return -2; }
}
int mm_get_stats(void* h, mm_stats* out) noexcept {
    if (!h || !out) return -1; *out = static_cast<Media*>(h)->stats(); return 0;
}
void mm_destroy(void* h) noexcept { try { delete static_cast<Media*>(h); } catch (...) {} }
