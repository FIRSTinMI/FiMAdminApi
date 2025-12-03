using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;

namespace FiMAdminApi.Services
{
    public class ThumbnailService
    {
        private const int ImageWidth = 1920;
        private const int ImageHeight = 1080;
        private const float LogoSizeRatio = 0.5f;
        private static FontFamily? _robotoFontFamily;

        public async Task<byte[]> DrawThumbnailAsync(string programType, string? line1, string? line2, string? line3)
        {
            var accentColor = programType.Equals("FTC", StringComparison.InvariantCultureIgnoreCase) ? Color.Parse("#f57e25") : Color.Parse("#009cd7");

            using var image = new Image<Rgba32>(Configuration.Default, ImageWidth, ImageHeight);

            // White background
            image.Mutate(ctx => ctx.Fill(Color.White));

            // Angled color bar on left side
            var p1 = new PointF(0, 0);
            var p2 = new PointF(0, ImageHeight);
            var p3 = new PointF(ImageWidth * 0.05f, ImageHeight);
            var p4 = new PointF(ImageWidth * 0.2f, 0);
            image.Mutate(ctx => ctx.FillPolygon(accentColor, p1, p2, p3, p4));
            
            var baseDir = Path.Combine(AppContext.BaseDirectory, "Assets");
            var logoFile = Path.Combine(baseDir, "fim-logo-blackonwhite.png");
            var fontFile = Path.Combine(baseDir, "Roboto-VariableFont_wdth,wght.ttf");
            
            Image<Rgba32>? logo = null;
            if (File.Exists(logoFile))
            {
                try
                {
                    logo = Image.Load<Rgba32>(logoFile);
                }
                catch
                {
                    // ignore
                }
            }

            if (_robotoFontFamily is null)
            {
                if (File.Exists(fontFile))
                {
                    var fontCollection = new FontCollection();
                    fontCollection.Add(fontFile);

                    _robotoFontFamily = fontCollection.Get("Roboto");
                }
                else
                {
                    throw new FontFamilyNotFoundException("Roboto");
                }
            }

            if (logo != null)
            {
                const float logoHeight = ImageHeight * LogoSizeRatio;
                var logoRatio = logo.Width / (float)logo.Height;
                var logoWidth = logoHeight * logoRatio;

                const float centerX = ImageWidth * 0.55f;
                const float centerY = ImageHeight * 0.3f;
                var logoX = centerX - (logoWidth / 2f);
                const float logoY = centerY - (logoHeight / 2f);

                // Resize the logo to the target size before drawing so we don't draw the full-size image
                using var resizedLogo = logo.Clone(ctx => ctx.Resize(new ResizeOptions
                {
                    Size = new Size((int)Math.Round(logoWidth), (int)Math.Round(logoHeight)),
                    Mode = ResizeMode.Max
                }));

                image.Mutate(ctx => ctx.DrawImage(resizedLogo, new Point((int)Math.Round(logoX), (int)Math.Round(logoY)), 1f));
            }
            
            var programFont = _robotoFontFamily.Value.CreateFont(ImageHeight * 0.05f, FontStyle.Regular);
            var titleFont = _robotoFontFamily.Value.CreateFont(ImageHeight * 0.1f, FontStyle.Bold);
            var subtitleFont = _robotoFontFamily.Value.CreateFont(ImageHeight * 0.07f, FontStyle.Regular);

            const float centerXPoint = ImageWidth * 0.55f;

            image.Mutate(ctx =>
            {
                var richOptions = new RichTextOptions(titleFont)
                {
                    WrappingLength = ImageWidth * 0.8f, // Use 80% of the canvas width
                    TabWidth = 4,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    // Draw line 1 (program)
                    Origin = new PointF(centerXPoint, ImageHeight * 0.55f - programFont.Size),
                    Font = programFont
                };

                var progText = line1 ?? programType;
                ctx.DrawText(richOptions, progText, Color.Black);

                // Draw line 2 (title)
                richOptions.Origin = new PointF(centerXPoint, ImageHeight * 0.77f - (titleFont.Size * 0.9f));
                richOptions.VerticalAlignment = VerticalAlignment.Center;
                richOptions.Font = titleFont;
                var rawTitle = (line2 ?? string.Empty).Replace("--", "—");
                ctx.DrawText(richOptions, rawTitle, Color.Black);


                // Draw line 3 (subtitle)
                richOptions.Origin = new PointF(centerXPoint, ImageHeight * 0.85f - (subtitleFont.Size * 0.1f));
                richOptions.Font = subtitleFont;
                richOptions.VerticalAlignment = VerticalAlignment.Center;
                var subtitleText = (line3 ?? string.Empty).Replace("--", "—");
                ctx.DrawText(richOptions, subtitleText, Color.Black);
            });

            using var ms = new MemoryStream();
            await image.SaveAsPngAsync(ms).ConfigureAwait(false);
            return ms.ToArray();
        }
    }
}
