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
                var richOptions = new RichTextOptions(titleFont)
                {
                    WrappingLength = IMAGE_WIDTH * 0.8f, // Use 80% of the canvas width
                    TabWidth = 4,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                };

                // Draw line 1 (program)
                richOptions.Origin = new PointF(centerXPoint, IMAGE_HEIGHT * 0.51f - programFont.Size);
                richOptions.Font = programFont;
                var progText = line1 ?? programType ?? string.Empty;
                ctx.DrawText(richOptions, progText, Color.Black);

                // Draw line 2 (title)
                richOptions.Origin = new PointF(centerXPoint, IMAGE_HEIGHT * 0.65f - (titleFont.Size * 0.9f));
                richOptions.Font = titleFont;
                var rawTitle = (line2 ?? string.Empty).Replace("--", "—");
                ctx.DrawText(richOptions, rawTitle, Color.Black);


                // Draw line 3 (subtitle)
                richOptions.Origin = new PointF(centerXPoint, IMAGE_HEIGHT * 0.85f - (subtitleFont.Size * 0.1f));
                richOptions.Font = subtitleFont;
                var subtitleText = (line3 ?? string.Empty).Replace("--", "—");
                ctx.DrawText(richOptions, subtitleText, Color.Black);
            });

            using var ms = new MemoryStream();
            await image.SaveAsPngAsync(ms).ConfigureAwait(false);
            return ms.ToArray();
        }
    }
}
