using openLuo.Core.Models;
using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Llm.Core.Models;
using openLuo.playgraound.Infrastructure;
using SkiaSharp;

namespace openLuo.playgraound.Demos.Llm;

/// <summary>
/// Multimodal image recognition test.
/// Bypasses the entire QQbot→session→agent pipeline and directly tests
/// whether the LLM module correctly sends image data as data URIs.
///
/// Usage:
///   dotnet run --project openLuo.playgraound -- multimodal [path/to/image.jpg]
///
/// Always runs a 50×50 color block test first.
/// If an image path is provided, tests at multiple resolutions:
///   256px → 512px → 768px → 1024px → original
///
/// The LLM config is read from config/llm.demo.ini.
/// </summary>
internal static class MultimodalImageDemo
{
    private const int JpegQuality = 70;

    public static async Task<int> RunAsync(string? imagePath)
    {
        var client = LlmDemoBootstrap.TryCreateClient(out var error);
        if (client is null)
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        // ── Phase 1: 50×50 color block test ──
        Console.WriteLine("═══════════════════════════════════════");
        Console.WriteLine("Phase 1: 50×50 纯色块测试 (验证管线)");
        Console.WriteLine("═══════════════════════════════════════");
        var ok = await RunImageTestAsync(client!, GenerateColorBlockImage(), "50x50_red_block.png",
            "这是一个 50x50 像素的纯红色方块。请用一句话描述它的颜色，用中文回答。");
        Console.WriteLine();

        if (!ok)
        {
            Console.Error.WriteLine("Phase 1 failed — 管线本身有问题，跳过后续测试。");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(imagePath))
        {
            Console.WriteLine("No image path provided. Only ran color block test.");
            Console.WriteLine("Usage: dotnet run --project openLuo.playgraound -- multimodal <image_path>");
            return 0;
        }

        if (!File.Exists(imagePath))
        {
            Console.Error.WriteLine($"Image not found: {imagePath}");
            return 1;
        }

        // ── Phase 2: Multi-resolution test ──
        var originalBytes = await File.ReadAllBytesAsync(imagePath);
        var mimeType = InferMimeType(imagePath);
        Console.WriteLine($"Original image: {Path.GetFileName(imagePath)} ({originalBytes.Length} bytes, {mimeType})");
        Console.WriteLine();

        int[] sizes = [256, 512, 768, 1024];
        foreach (var maxDim in sizes)
        {
            Console.WriteLine("───────────────────────────────────────");
            Console.WriteLine($"Phase 2.{Array.IndexOf(sizes, maxDim) + 1}: Resize max {maxDim}px (JPEG q{JpegQuality})");
            Console.WriteLine("───────────────────────────────────────");

            var resized = ResizeAndCompress(originalBytes, maxDim, JpegQuality);
            Console.WriteLine($"Resized: {resized.Bytes.Length} bytes → Data URI: {resized.Bytes.Length * 4 / 3 + 50} chars est.");

            var testOk = await RunImageTestAsync(client!, resized, $"resized_{maxDim}px.jpg",
                $"请描述你在这张图片中看到了什么内容。用中文回答，简洁一点。");

            Console.WriteLine(testOk ? "  => PASS" : "  => FAIL");
            Console.WriteLine();

            if (!testOk)
            {
                Console.Error.WriteLine($"Stopping at {maxDim}px — larger sizes will also fail.");
                return 1;
            }
        }

        // Original size (no resize)
        Console.WriteLine("───────────────────────────────────────");
        Console.WriteLine("Phase 2.5: 原始图片 (无压缩)");
        Console.WriteLine("───────────────────────────────────────");
        Console.WriteLine($"Original: {originalBytes.Length} bytes → Data URI: {originalBytes.Length * 4 / 3 + 50} chars est.");

        var origOk = await RunImageTestAsync(client!, (originalBytes, mimeType), Path.GetFileName(imagePath),
            "请描述你在这张图片中看到了什么内容。用中文回答，简洁一点。");

        Console.WriteLine(origOk ? "  => PASS" : "  => FAIL (可能 payload 太大)");
        Console.WriteLine();

        Console.WriteLine("═══════════════════════════════════════");
        Console.WriteLine("测试完成。找到能通过的最大分辨率后，在主应用中设置对应的 MaxImageDimension。");
        Console.WriteLine("═══════════════════════════════════════");
        return 0;
    }

    private static (byte[] Bytes, string MimeType) ResizeAndCompress(byte[] input, int maxDimension, int quality)
    {
        using var original = SKBitmap.Decode(input);
        int newW = original.Width;
        int newH = original.Height;
        if (newW > maxDimension || newH > maxDimension)
        {
            float scale = Math.Min((float)maxDimension / newW, (float)maxDimension / newH);
            newW = (int)(newW * scale);
            newH = (int)(newH * scale);
        }

        using var resized = original.Resize(
            new SKImageInfo(newW, newH),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return (data.ToArray(), "image/jpeg");
    }

    private static async Task<bool> RunImageTestAsync(
        ILlmClient client,
        (byte[] Bytes, string MimeType) image,
        string name,
        string prompt)
    {
        var dataUri = $"data:{image.MimeType};base64,{Convert.ToBase64String(image.Bytes)}";

        Console.WriteLine($"Data URI: {dataUri.Length} chars ({(dataUri.Length + 1023) / 1024} KB)");
        PrintBase64Sample(dataUri);

        var imageBlock = new ImageBlock
        {
            Kind = BlockKind.Image,
            AssetId = dataUri,
            MimeType = image.MimeType,
            Name = name,
            AltText = "测试图片",
            DataUri = dataUri
        };

        var blocks = new List<Block>
        {
            new TextBlock { Kind = BlockKind.Text, Text = prompt },
            imageBlock
        };
        var userMessage = new ChatMessage(ChatMessageRole.User, blocks);

        Console.WriteLine($"DebugContent: {userMessage.DebugContent}");

        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var options = new openLuo.Modules.Llm.Core.Models.LlmOptions { EnableMultimodal = true };
            var reply = await client.CompleteAsync([userMessage], options, cts.Token);
            var elapsed = DateTimeOffset.UtcNow - startedAt;
            Console.WriteLine($"Response ({elapsed.TotalSeconds:F1}s): {reply.Content.Trim()}");
            return !string.IsNullOrWhiteSpace(reply.Content);
        }
        catch (Exception ex)
        {
            var elapsed = DateTimeOffset.UtcNow - startedAt;
            Console.Error.WriteLine($"Failed after {elapsed.TotalSeconds:F1}s: {ex.Message}");
            return false;
        }
    }

    private static (byte[] Bytes, string MimeType) GenerateColorBlockImage(int width = 50, int height = 50)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 80);
        return (data.ToArray(), "image/png");
    }

    private static void PrintBase64Sample(string dataUri)
    {
        var commaIdx = dataUri.IndexOf(',');
        if (commaIdx >= 0 && commaIdx < dataUri.Length - 1)
        {
            var base64Part = dataUri[(commaIdx + 1)..];
            var sample = base64Part.Length > 40 ? base64Part[..40] : base64Part;
            Console.WriteLine($"Base64 sample: {sample}...");
        }
    }

    private static string InferMimeType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/jpeg"
        };
    }
}
