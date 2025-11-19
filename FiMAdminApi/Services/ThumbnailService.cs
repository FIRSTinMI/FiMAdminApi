using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;

namespace FiMAdminApi.Services
{
    public class ThumbnailService
    {
        private const int IMAGE_WIDTH = 1920;
        private const int IMAGE_HEIGHT = 1080;
        private const float LOGO_SIZE_RATIO = 0.5f;

        public async Task<byte[]> DrawThumbnailAsync(string programType, string line1, string line2, string line3)
        {
            var accentColor = (programType ?? string.Empty).ToUpperInvariant() == "FTC" ? Color.Parse("#f57e25") : Color.Parse("#009cd7");

            using var image = new Image<Rgba32>(Configuration.Default, IMAGE_WIDTH, IMAGE_HEIGHT);

            // White background
            image.Mutate(ctx => ctx.Fill(Color.White));

            // Angled color bar on left side
            var p1 = new PointF(0, 0);
            var p2 = new PointF(0, IMAGE_HEIGHT);
            var p3 = new PointF(IMAGE_WIDTH * 0.05f, IMAGE_HEIGHT);
            var p4 = new PointF(IMAGE_WIDTH * 0.2f, 0);
            image.Mutate(ctx => ctx.FillPolygon(accentColor, p1, p2, p3, p4));

            // Try to load a local logo from a few candidate paths
            Image<Rgba32>? logo = null;
            string baseDir = AppContext.BaseDirectory ?? string.Empty;

            var logoFile = System.IO.Path.Combine(baseDir, "Assets", "fim-logo-blackonwhite.png");
            System.Console.WriteLine($"Looking for logo file at: {logoFile}");

            if (File.Exists(logoFile))
            {
                try
                {
                    logo = SixLabors.ImageSharp.Image.Load<Rgba32>(logoFile);
                }
                catch
                {
                    // ignore
                }
            }

            if (logo != null)
            {
                var logoHeight = IMAGE_HEIGHT * LOGO_SIZE_RATIO;
                var logoRatio = logo.Width / (float)logo.Height;
                var logoWidth = logoHeight * logoRatio;

                var centerX = IMAGE_WIDTH * 0.55f;
                var centerY = IMAGE_HEIGHT * 0.3f;
                var logoX = centerX - (logoWidth / 2f);
                var logoY = centerY - (logoHeight / 2f);

                // Resize the logo to the target size before drawing so we don't draw the full-size image
                using var resizedLogo = logo.Clone(ctx => ctx.Resize(new SixLabors.ImageSharp.Processing.ResizeOptions
                {
                    Size = new SixLabors.ImageSharp.Size((int)Math.Round(logoWidth), (int)Math.Round(logoHeight)),
                    Mode = SixLabors.ImageSharp.Processing.ResizeMode.Max
                }));

                image.Mutate(ctx => ctx.DrawImage(resizedLogo, new Point((int)Math.Round(logoX), (int)Math.Round(logoY)), 1f));
            }

            // Prepare fonts
            var fontCollection = SystemFonts.Collection;
            var family = fontCollection.Families.FirstOrDefault(f => f.Name.Contains("Arial") || f.Name.Contains("Inter") || f.Name.Contains("Segoe"));

            var programFont = family.CreateFont(IMAGE_HEIGHT * 0.05f, FontStyle.Regular);
            var titleFont = family.CreateFont(IMAGE_HEIGHT * 0.1f, FontStyle.Bold);
            var subtitleFont = family.CreateFont(IMAGE_HEIGHT * 0.05f, FontStyle.Regular);

            var centerXPoint = IMAGE_WIDTH * 0.55f;

            image.Mutate(ctx =>
            {
                // Simple centered text using an approximate character width estimator
                float charFactor = 0.55f; // approximate width per font size

                var progText = (line1 ?? programType ?? string.Empty);
                var progEstWidth = programFont.Size * progText.Length * charFactor;
                var progPoint = new PointF(centerXPoint - progEstWidth / 2f, IMAGE_HEIGHT * 0.5f - programFont.Size / 2f);
                ctx.DrawText(progText, programFont, Color.Black, progPoint);

                // Title: wrap to max width (85% of canvas) and at most 2 lines
                var rawTitle = (line2 ?? string.Empty).Replace("--", "—");
                var maxWidthPx = IMAGE_WIDTH * 0.85f;
                float charFactorTitle = charFactor; // reuse estimator
                int maxCharsPerLine = Math.Max(1, (int)(maxWidthPx / (titleFont.Size * charFactorTitle)));

                string lineA = string.Empty;
                string lineB = string.Empty;
                if (rawTitle.Length <= maxCharsPerLine)
                {
                    lineA = rawTitle;
                }
                else
                {
                    var words = rawTitle.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    var sb = new System.Text.StringBuilder();
                    int i = 0;
                    for (; i < words.Length; i++)
                    {
                        var next = (sb.Length == 0) ? words[i] : sb + " " + words[i];
                        if (next.Length > maxCharsPerLine)
                            break;
                        if (sb.Length > 0) sb.Append(' ');
                        sb.Append(words[i]);
                    }
                    lineA = sb.ToString();

                    if (i < words.Length)
                    {
                        var sb2 = new System.Text.StringBuilder();
                        for (; i < words.Length; i++)
                        {
                            var next = (sb2.Length == 0) ? words[i] : sb2 + " " + words[i];
                            if (next.Length > maxCharsPerLine)
                            {
                                var remaining = string.Join(' ', words.Skip(i));
                                if (remaining.Length > maxCharsPerLine)
                                {
                                    lineB = remaining.Substring(0, Math.Max(0, maxCharsPerLine - 1)) + "…";
                                }
                                else
                                {
                                    lineB = remaining;
                                }
                                break;
                            }
                            if (sb2.Length > 0) sb2.Append(' ');
                            sb2.Append(words[i]);
                        }
                        if (string.IsNullOrEmpty(lineB) && sb2.Length > 0) lineB = sb2.ToString();
                    }
                }

                if (string.IsNullOrEmpty(lineB))
                {
                    var titleEstWidth = titleFont.Size * lineA.Length * charFactorTitle;
                    var titlePoint = new PointF(centerXPoint - titleEstWidth / 2f, IMAGE_HEIGHT * 0.65f - titleFont.Size / 2f);
                    ctx.DrawText(lineA, titleFont, Color.Black, titlePoint);
                }
                else
                {
                    var lineSpacing = titleFont.Size * 1.05f;
                    var lineAEst = titleFont.Size * lineA.Length * charFactorTitle;
                    var lineBEst = titleFont.Size * lineB.Length * charFactorTitle;
                    var lineAX = centerXPoint - lineAEst / 2f;
                    var lineBX = centerXPoint - lineBEst / 2f;
                    var centerY = IMAGE_HEIGHT * 0.65f;
                    var lineAY = centerY - (lineSpacing / 2f);
                    var lineBY = centerY + (lineSpacing / 2f);
                    ctx.DrawText(lineA, titleFont, Color.Black, new PointF(lineAX, lineAY));
                    ctx.DrawText(lineB, titleFont, Color.Black, new PointF(lineBX, lineBY));
                }

                var subtitleText = (line3 ?? string.Empty).Replace("--", "—");
                var subtitleEstWidth = subtitleFont.Size * subtitleText.Length * charFactor;
                var subtitlePoint = new PointF(centerXPoint - subtitleEstWidth / 2f, IMAGE_HEIGHT * 0.87f - subtitleFont.Size / 2f);
                ctx.DrawText(subtitleText, subtitleFont, Color.Black, subtitlePoint);
            });

            using var ms = new MemoryStream();
            await image.SaveAsPngAsync(ms).ConfigureAwait(false);
            return ms.ToArray();
        }
    }
}
