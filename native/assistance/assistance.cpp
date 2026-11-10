#include "llama.h"
#include "mtmd.h"
#include "mtmd-helper.h"
#include <algorithm>
#include <cstdio>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>
#ifdef __ANDROID__
#include <android/log.h>
#endif

static void stage(const char * message) {
#ifdef __ANDROID__
    __android_log_write(ANDROID_LOG_INFO, "DecompShelf.Help", message);
#endif
}

#ifdef _WIN32
#define DS_EXPORT extern "C" __declspec(dllexport)
#else
#define DS_EXPORT extern "C" __attribute__((visibility("default")))
#endif

using progress_fn = int (*)(const char *, void *);
struct progress_state {
    progress_fn callback;
    void * user;
    std::string text;
    bool cancelled() { return callback(text.c_str(), user) != 0; }
};

// One request owns all weights and context. RAII releases them on success, cancellation and errors.
static int generate(const char * path, const char * projector, const unsigned char * rgb, int width, int height,
                    const char * system, const char * user, progress_fn callback, void * state, char * error, int error_size) {
    try {
        if (!path || !system || !user || !callback) throw std::runtime_error("Invalid request");
        static std::once_flag initialized;
        std::call_once(initialized, [] { llama_backend_init(); });
        progress_state progress{callback, state, ""};
        if (projector && (!rgb || width < 1 || height < 1 || width > 1280 || height > 1280))
            throw std::runtime_error("Capture invalide.");
        auto params = llama_model_default_params();
        if (projector) stage("vision: loading model");
        params.n_gpu_layers = 0;
        params.progress_callback = [](float, void * p) { return !static_cast<progress_state *>(p)->cancelled(); };
        params.progress_callback_user_data = &progress;
        std::unique_ptr<llama_model, decltype(&llama_model_free)> model(llama_model_load_from_file(path, params), llama_model_free);
        if (!model) throw std::runtime_error("Chargement du modèle impossible ou annulé.");
        if (projector) stage("vision: model loaded");
        const std::string visual_user = projector ? std::string(mtmd_default_marker()) + "\n" + user : user;
        const llama_chat_message messages[] = {{"system", system}, {"user", visual_user.c_str()}};
        const auto tmpl = llama_model_chat_template(model.get(), nullptr);
        std::vector<char> formatted(16384);
        auto size = llama_chat_apply_template(tmpl, messages, 2, true, formatted.data(), formatted.size());
        if (size < 0 || size > 16384) throw std::runtime_error("Format de conversation non pris en charge.");
        std::string prompt(formatted.data(), size);
        // LFM3's official enable_thinking=false prefix keeps the hint brief and user-facing.
        if (prompt.size() >= 7 && prompt.compare(prompt.size() - 7, 7, "<think>") == 0) prompt += "</think>";
        else prompt += "<think></think>";
        const auto vocab = llama_model_get_vocab(model.get());
        int count = -llama_tokenize(vocab, prompt.c_str(), prompt.size(), nullptr, 0, true, true);
        if (count <= 0 || count > 3584) throw std::runtime_error("Question trop longue pour le contexte local.");
        std::vector<llama_token> tokens(count);
        if (llama_tokenize(vocab, prompt.c_str(), prompt.size(), tokens.data(), count, true, true) != count)
            throw std::runtime_error("Tokenisation impossible.");
        auto context_params = llama_context_default_params();
        context_params.n_ctx = 4096;
        context_params.n_batch = 128;
        context_params.n_ubatch = 128;
        context_params.n_threads = std::max(1u, std::min(4u, std::thread::hardware_concurrency()));
        context_params.n_threads_batch = context_params.n_threads;
        context_params.abort_callback = [](void * p) { return static_cast<progress_state *>(p)->cancelled(); };
        context_params.abort_callback_data = &progress;
        std::unique_ptr<llama_context, decltype(&llama_free)> ctx(llama_init_from_model(model.get(), context_params), llama_free);
        if (!ctx) throw std::runtime_error("Mémoire insuffisante pour le contexte IA.");
        std::unique_ptr<llama_sampler, decltype(&llama_sampler_free)> sampler(llama_sampler_init_greedy(), llama_sampler_free);
        if (projector) {
            auto vision_params = mtmd_context_params_default();
            vision_params.use_gpu = false; vision_params.n_threads = context_params.n_threads;
            vision_params.warmup = false; vision_params.image_max_tokens = 1024;
            vision_params.progress_callback = [](float, void * p) { return !static_cast<progress_state *>(p)->cancelled(); };
            vision_params.progress_callback_user_data = &progress;
            vision_params.cb_eval = [](ggml_tensor *, bool ask, void * p) {
                const bool cancelled = static_cast<progress_state *>(p)->cancelled();
                return ask ? cancelled : !cancelled;
            };
            vision_params.cb_eval_user_data = &progress;
            std::unique_ptr<mtmd_context, decltype(&mtmd_free)> vision(mtmd_init_from_file(projector, model.get(), vision_params), mtmd_free);
            if (!vision || !mtmd_support_vision(vision.get())) throw std::runtime_error("Chargement de la vision impossible ou annulé.");
            stage("vision: projector loaded");
            std::unique_ptr<mtmd_bitmap, decltype(&mtmd_bitmap_free)> bitmap(mtmd_bitmap_init(width, height, rgb), mtmd_bitmap_free);
            std::unique_ptr<mtmd_input_chunks, decltype(&mtmd_input_chunks_free)> chunks(mtmd_input_chunks_init(), mtmd_input_chunks_free);
            const mtmd_input_text text{prompt.c_str(), prompt.size(), true, true};
            const mtmd_bitmap * images[] = {bitmap.get()};
            if (!bitmap || !chunks || mtmd_tokenize(vision.get(), chunks.get(), &text, images, 1) != 0)
                throw std::runtime_error("Lecture de la capture impossible.");
            if (mtmd_helper_get_n_tokens(chunks.get()) > 3584) throw std::runtime_error("Capture trop grande pour le contexte local.");
            llama_pos past = 0;
            if (progress.cancelled()) return 2;
            stage("vision: evaluating image");
            if (mtmd_helper_eval_chunks(vision.get(), ctx.get(), chunks.get(), 0, 0, 128, true, &past) != 0)
                throw std::runtime_error("Analyse de la capture interrompue.");
            stage("vision: image evaluated");
        } else for (int start = 0; start < count; start += 128) {
            if (progress.cancelled()) return 2;
            auto batch = llama_batch_get_one(tokens.data() + start, std::min(128, count - start));
            if (llama_decode(ctx.get(), batch) != 0) throw std::runtime_error("Préparation de la réponse interrompue.");
        }
        if (projector) stage("vision: generating answer");
        for (int generated = 0; generated < 384; ++generated) {
            if (progress.cancelled()) return 2;
            auto token = llama_sampler_sample(sampler.get(), ctx.get(), -1);
            if (llama_vocab_is_eog(vocab, token)) break;
            std::vector<char> piece(256);
            int n = llama_token_to_piece(vocab, token, piece.data(), piece.size(), 0, false);
            if (n < 0) { piece.resize(-n); n = llama_token_to_piece(vocab, token, piece.data(), piece.size(), 0, false); }
            if (n < 0) throw std::runtime_error("Décodage de la réponse impossible.");
            progress.text.append(piece.data(), n);
            if (progress.cancelled()) return 2;
            auto batch = llama_batch_get_one(&token, 1);
            if (llama_decode(ctx.get(), batch) != 0) throw std::runtime_error("Génération interrompue.");
        }
        if (projector) stage("vision: request complete");
        return 0;
    } catch (const std::exception & ex) {
        if (error && error_size > 0) std::snprintf(error, error_size, "%s", ex.what());
        return 1;
    } catch (...) {
        if (error && error_size > 0) std::snprintf(error, error_size, "Erreur du moteur IA local.");
        return 1;
    }
}

DS_EXPORT int ds_ai_generate(const char * path, const char * system, const char * user,
                            progress_fn callback, void * state, char * error, int error_size) {
    return generate(path, nullptr, nullptr, 0, 0, system, user, callback, state, error, error_size);
}

DS_EXPORT int ds_ai_generate_screen(const char * path, const char * projector, const unsigned char * rgb, int width, int height,
                                   const char * system, const char * user, progress_fn callback, void * state, char * error, int error_size) {
    if (!projector) return 1;
    return generate(path, projector, rgb, width, height, system, user, callback, state, error, error_size);
}
