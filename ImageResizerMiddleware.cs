// ImageResizerMiddleware.cs
using Microsoft.Extensions.Caching.Memory;
using SkiaSharp;
using System.Text;
using System.Reflection;

namespace SkiaSharpExample
{
    public class ImageResizerMiddleware
    {
        struct ResizeParams
        {
            public bool hasParams;
            public int w;
            public int h;
            public bool autorotate;
            public int quality;
            public string format;
            public string mode;
            public static string[] modes = new string[] { "pad", "max", "crop", "stretch" };

            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.Append($"w: {w}, ");
                sb.Append($"h: {h}, ");
                sb.Append($"autorotate: {autorotate}, ");
                sb.Append($"quality: {quality}, ");
                sb.Append($"format: {format}, ");
                sb.Append($"mode: {mode}");
                return sb.ToString();
            }
        }

        private readonly RequestDelegate _next;
        private readonly ILogger<ImageResizerMiddleware> _logger;
        private readonly Microsoft.AspNetCore.Hosting.IHostingEnvironment _env;
        private readonly IMemoryCache _memoryCache;
        private static readonly string[] suffixes = new string[] { ".png", ".jpg", ".jpeg" };

        public ImageResizerMiddleware(RequestDelegate next, Microsoft.AspNetCore.Hosting.IHostingEnvironment env, ILogger<ImageResizerMiddleware> logger, IMemoryCache memoryCache)
        {
            _next = next;
            _env = env;
            _logger = logger;
            _memoryCache = memoryCache;
        }

        public async Task Invoke(HttpContext context)
        {
            var path = context.Request.Path;
            if (context.Request.Query.Count == 0 || !IsImagePath(path))
            {
                await _next.Invoke(context);
                return;
            }

            var resizeParams = GetResizeParams(path, context.Request.Query);
            if (!resizeParams.hasParams || (resizeParams.w == 0 && resizeParams.h == 0))
            {
                await _next.Invoke(context);
                return;
            }

            _logger.LogInformation($"Resizing {path.Value} with params {resizeParams}");

            // Process image
            var filePath = Path.Combine(_env.WebRootPath, path.Value.TrimStart('/'));
            if (File.Exists(filePath))
            {
                using (var inputStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (var original = SKBitmap.Decode(inputStream))
                {
                    var resizedImage = ResizeImage(original, resizeParams);
                    using (var outputStream = new MemoryStream())
                    {
                        var imageFormat = SKEncodedImageFormat.Png;
                        switch (resizeParams.format.ToLower())
                        {
                            case "jpg":
                            case "jpeg":
                                imageFormat = SKEncodedImageFormat.Jpeg;
                                break;
                            case "png":
                                imageFormat = SKEncodedImageFormat.Png;
                                break;
                        }

                        resizedImage.Encode(outputStream, imageFormat, resizeParams.quality);
                        context.Response.ContentType = $"image/{resizeParams.format}";
                        await context.Response.Body.WriteAsync(outputStream.ToArray());
                        return;
                    }
                }
            }

            await _next.Invoke(context);
        }

        private bool IsImagePath(PathString path)
        {
            if (path == null || !path.HasValue) return false;
            return suffixes.Any(x => path.Value.EndsWith(x, StringComparison.OrdinalIgnoreCase));
        }

        private ResizeParams GetResizeParams(PathString path, IQueryCollection query)
        {
            ResizeParams resizeParams = new ResizeParams();
            resizeParams.hasParams = resizeParams.GetType().GetTypeInfo().GetFields().Where(f => f.Name != "hasParams").Any(f => query.ContainsKey(f.Name));

            if (!resizeParams.hasParams) return resizeParams;

            if (query.ContainsKey("format")) resizeParams.format = query["format"];
            else resizeParams.format = path.Value.Substring(path.Value.LastIndexOf('.') + 1);

            if (query.ContainsKey("autorotate")) bool.TryParse(query["autorotate"], out resizeParams.autorotate);
            int quality = 100;
            if (query.ContainsKey("quality")) int.TryParse(query["quality"], out quality);
            resizeParams.quality = quality;

            int w = 0;
            if (query.ContainsKey("w")) int.TryParse(query["w"], out w);
            resizeParams.w = w;

            int h = 0;
            if (query.ContainsKey("h")) int.TryParse(query["h"], out h);
            resizeParams.h = h;

            resizeParams.mode = "max";
            if (h != 0 && w != 0 && query.ContainsKey("mode") && ResizeParams.modes.Any(m => query["mode"] == m)) resizeParams.mode = query["mode"];

            return resizeParams;
        }

        private SKBitmap ResizeImage(SKBitmap original, ResizeParams resizeParams)
        {
            int width = resizeParams.w;
            int height = resizeParams.h;

            if (resizeParams.mode == "max")
            {
                float ratio = Math.Min((float)resizeParams.w / original.Width, (float)resizeParams.h / original.Height);
                width = (int)(original.Width * ratio);
                height = (int)(original.Height * ratio);
            }

            var resized = new SKBitmap(width, height);
            using (var canvas = new SKCanvas(resized))
            {
                canvas.Clear(SKColors.White);
                canvas.DrawBitmap(original, new SKRect(0, 0, width, height));
            }

            return resized;
        }
    }
}