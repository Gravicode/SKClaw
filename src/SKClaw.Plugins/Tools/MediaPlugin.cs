using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;

namespace SKClaw.Plugins.Tools;

/// <summary>
/// MediaPlugin — Image generation (DALL-E / Stable Diffusion), image analysis,
/// audio transcription (Whisper), text-to-speech, and PDF/document helpers.
/// All API keys are taken from AppConfiguration where applicable.
/// </summary>
public class MediaPlugin
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };
    private readonly string _openAiKey;
    private readonly string _stabilityKey;
    private readonly string _workspace;

    public MediaPlugin(string openAiKey = "", string stabilityKey = "", string workspace = "")
    {
        _openAiKey    = openAiKey;
        _stabilityKey = stabilityKey;
        _workspace    = string.IsNullOrEmpty(workspace)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".skclaw", "media")
            : workspace;
        Directory.CreateDirectory(_workspace);
    }

    // ── Image Generation ───────────────────────────────────────

    [KernelFunction, Description("Generate an image using DALL-E 3 (requires OpenAI API key)")]
    public async Task<string> GenerateImageDalleAsync(
        [Description("Detailed description of the image to generate")] string prompt,
        [Description("Image size: 1024x1024, 1792x1024, 1024x1792")] string size = "1024x1024",
        [Description("Quality: standard or hd")] string quality = "standard",
        [Description("Style: vivid or natural")] string style = "vivid",
        [Description("Save to file? If yes, returns local path")] bool saveToFile = false)
    {
        if (string.IsNullOrEmpty(_openAiKey))
            return "OpenAI API key not configured (LLM:OpenAI:ApiKey in app.config).";

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/images/generations");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _openAiKey);
        var body = JsonSerializer.Serialize(new { model = "dall-e-3", prompt, n = 1, size, quality, style });
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var res = await _http.SendAsync(req);
        var json = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode) return $"DALL-E error: {json}";

        var doc = JsonDocument.Parse(json);
        var url = doc.RootElement.GetProperty("data")[0].GetProperty("url").GetString() ?? "";
        var revised = doc.RootElement.GetProperty("data")[0].TryGetProperty("revised_prompt", out var rp) ? rp.GetString() : "";

        if (saveToFile)
        {
            var filename = $"dalle_{DateTime.Now:yyyyMMddHHmmss}.png";
            var path = Path.Combine(_workspace, filename);
            var bytes = await _http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(path, bytes);
            return $"✅ Image saved: {path}\nRevised prompt: {revised}\nURL: {url}";
        }

        return $"✅ Image URL: {url}{(string.IsNullOrEmpty(revised) ? "" : $"\nRevised prompt: {revised}")}";
    }

    [KernelFunction, Description("Generate an image using Stability AI (Stable Diffusion) API")]
    public async Task<string> GenerateImageStabilityAsync(
        [Description("Image description")] string prompt,
        [Description("Negative prompt (what to avoid)")] string negativePrompt = "blurry, low quality, distorted",
        [Description("Model: stable-diffusion-xl-1024-v1-0, stable-diffusion-v1-6")] string model = "stable-diffusion-xl-1024-v1-0",
        [Description("Width in pixels (multiple of 64)")] int width = 1024,
        [Description("Height in pixels (multiple of 64)")] int height = 1024,
        [Description("Number of inference steps (10-50)")] int steps = 30,
        [Description("Guidance scale (1-35)")] double cfgScale = 7.0)
    {
        if (string.IsNullOrEmpty(_stabilityKey))
            return "Stability AI API key not configured.";

        var payload = new
        {
            text_prompts = new[]
            {
                new { text = prompt, weight = 1.0 },
                new { text = negativePrompt, weight = -1.0 }
            },
            cfg_scale = cfgScale,
            height, width,
            samples = 1,
            steps
        };

        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.stability.ai/v1/generation/{model}/text-to-image");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _stabilityKey);
        req.Headers.Accept.ParseAdd("application/json");
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var res = await _http.SendAsync(req);
        var json = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode) return $"Stability AI error: {json}";

        var doc = JsonDocument.Parse(json);
        var artifacts = doc.RootElement.GetProperty("artifacts").EnumerateArray().ToList();
        if (artifacts.Count == 0) return "No image returned.";

        var b64 = artifacts[0].GetProperty("base64").GetString() ?? "";
        var filename = $"sdiff_{DateTime.Now:yyyyMMddHHmmss}.png";
        var path = Path.Combine(_workspace, filename);
        await File.WriteAllBytesAsync(path, Convert.FromBase64String(b64));
        return $"✅ Image saved: {path} ({width}x{height}, {steps} steps)";
    }

    // ── Image Analysis ─────────────────────────────────────────

    [KernelFunction, Description("Analyse an image from a URL using OpenAI Vision (GPT-4o)")]
    public async Task<string> AnalyseImageUrlAsync(
        [Description("Image URL")] string imageUrl,
        [Description("Question or instruction about the image")] string question = "Describe this image in detail.",
        [Description("Detail level: low, high, auto")] string detail = "auto")
    {
        if (string.IsNullOrEmpty(_openAiKey))
            return "OpenAI API key not configured.";

        var body = new
        {
            model = "gpt-4o",
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = question },
                        new { type = "image_url", image_url = new { url = imageUrl, detail } }
                    }
                }
            },
            max_tokens = 1024
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _openAiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var res = await _http.SendAsync(req);
        var json = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode) return $"Vision API error: {json}";

        var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("choices")[0]
            .GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    [KernelFunction, Description("Analyse a local image file using OpenAI Vision")]
    public async Task<string> AnalyseLocalImageAsync(
        [Description("Path to the image file in workspace")] string filePath,
        [Description("Question about the image")] string question = "Describe this image in detail.")
    {
        if (string.IsNullOrEmpty(_openAiKey))
            return "OpenAI API key not configured.";

        var fullPath = Path.Combine(_workspace, filePath);
        if (!File.Exists(fullPath)) return $"File not found: {filePath}";

        var ext  = Path.GetExtension(filePath).ToLower().TrimStart('.');
        var mime = ext switch { "jpg" or "jpeg" => "image/jpeg", "png" => "image/png", "gif" => "image/gif", "webp" => "image/webp", _ => "image/png" };
        var b64  = Convert.ToBase64String(await File.ReadAllBytesAsync(fullPath));
        var dataUrl = $"data:{mime};base64,{b64}";

        return await AnalyseImageUrlAsync(dataUrl, question, "high");
    }

    [KernelFunction, Description("Extract text from an image (OCR) using OpenAI Vision")]
    public async Task<string> ExtractTextFromImageAsync(
        [Description("Image URL or local file path")] string imageSource,
        [Description("Language hint, e.g. 'English', 'Indonesian', 'mixed'")] string language = "auto-detect")
    {
        var question = $"Extract ALL text visible in this image exactly as it appears. Language: {language}. Return only the extracted text, preserving layout as much as possible.";

        if (imageSource.StartsWith("http"))
            return await AnalyseImageUrlAsync(imageSource, question);

        return await AnalyseLocalImageAsync(imageSource, question);
    }

    // ── Audio ──────────────────────────────────────────────────

    [KernelFunction, Description("Transcribe audio to text using OpenAI Whisper API")]
    public async Task<string> TranscribeAudioAsync(
        [Description("Path to audio file (mp3, mp4, wav, m4a, webm, ogg) in workspace")] string filePath,
        [Description("Language hint, e.g. 'en', 'id', 'fr' (empty = auto-detect)")] string language = "",
        [Description("Response format: text, verbose_json, srt, vtt")] string format = "text")
    {
        if (string.IsNullOrEmpty(_openAiKey))
            return "OpenAI API key not configured.";

        var fullPath = Path.Combine(_workspace, filePath);
        if (!File.Exists(fullPath)) return $"File not found: {filePath}";

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(await File.ReadAllBytesAsync(fullPath)), "file", Path.GetFileName(fullPath));
        form.Add(new StringContent("whisper-1"), "model");
        form.Add(new StringContent(format), "response_format");
        if (!string.IsNullOrEmpty(language)) form.Add(new StringContent(language), "language");

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions")
        {
            Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _openAiKey) },
            Content = form
        };

        var res = await _http.SendAsync(req);
        var result = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode) return $"Whisper error: {result}";

        return format == "text" ? result : result;
    }

    [KernelFunction, Description("Convert text to speech using OpenAI TTS API and save to file")]
    public async Task<string> TextToSpeechAsync(
        [Description("Text to convert to speech")] string text,
        [Description("Voice: alloy, echo, fable, onyx, nova, shimmer")] string voice = "nova",
        [Description("Model: tts-1 (faster) or tts-1-hd (better quality)")] string model = "tts-1",
        [Description("Output format: mp3, opus, aac, flac")] string format = "mp3",
        [Description("Speed: 0.25 to 4.0 (1.0 = normal)")] double speed = 1.0)
    {
        if (string.IsNullOrEmpty(_openAiKey))
            return "OpenAI API key not configured.";

        var body = JsonSerializer.Serialize(new { model, input = text, voice, response_format = format, speed });
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/speech");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _openAiKey);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode) return $"TTS error: {await res.Content.ReadAsStringAsync()}";

        var filename = $"tts_{DateTime.Now:yyyyMMddHHmmss}.{format}";
        var path = Path.Combine(_workspace, filename);
        await File.WriteAllBytesAsync(path, await res.Content.ReadAsByteArrayAsync());
        return $"✅ Audio saved: {path} ({text.Length} chars, voice={voice}, speed={speed}x)";
    }

    // ── PDF / Document helpers ─────────────────────────────────

    [KernelFunction, Description("Extract text content from a PDF file using pdftotext (if installed) or basic parsing")]
    public async Task<string> ExtractPdfTextAsync(
        [Description("PDF file path in workspace")] string filePath,
        [Description("Max characters to return")] int maxChars = 8000)
    {
        var full = Path.Combine(_workspace, filePath);
        if (!File.Exists(full)) return $"File not found: {filePath}";

        // Try pdftotext (poppler-utils)
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("pdftotext", $"\"{full}\" -")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc != null)
            {
                var text = await proc.StandardOutput.ReadToEndAsync();
                proc.WaitForExit();
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Length > maxChars ? text[..maxChars] + "\n...[truncated]" : text;
            }
        }
        catch { /* pdftotext not available, fall through */ }

        return "PDF text extraction requires 'pdftotext' (poppler-utils). Install it and retry, or use the PDF skill for .NET-based extraction.";
    }

    [KernelFunction, Description("Download an image from a URL and save it to the media workspace")]
    public async Task<string> DownloadImageAsync(
        [Description("Image URL")] string url,
        [Description("Filename to save as (e.g. photo.jpg)")] string filename)
    {
        try
        {
            var bytes = await _http.GetByteArrayAsync(url);
            var path = Path.Combine(_workspace, filename);
            await File.WriteAllBytesAsync(path, bytes);
            return $"✅ Downloaded {bytes.Length / 1024:N0} KB → {path}";
        }
        catch (Exception ex) { return $"Download failed: {ex.Message}"; }
    }

    [KernelFunction, Description("Resize or convert an image file using metadata info (returns info for further processing)")]
    public string GetImageInfo([Description("Image file path in workspace")] string filePath)
    {
        var full = Path.Combine(_workspace, filePath);
        if (!File.Exists(full)) return $"File not found: {filePath}";
        var fi = new FileInfo(full);
        return $"""
            File    : {fi.Name}
            Size    : {fi.Length / 1024:N0} KB
            Modified: {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}
            Extension: {fi.Extension}
            Full path: {full}
            """;
    }
}
