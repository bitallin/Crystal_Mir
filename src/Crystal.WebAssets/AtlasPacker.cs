namespace Crystal.WebAssets;

public sealed class AtlasPlacement
{
    public required int ImageIndex { get; init; }
    public required int PageIndex { get; init; }
    public required string PageFile { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
}

public sealed class AtlasPage
{
    public required int PageIndex { get; init; }
    public required string PageFile { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required byte[] Rgba { get; init; }
}

public sealed class AtlasResult
{
    public required IReadOnlyList<AtlasPage> Pages { get; init; }
    public required IReadOnlyList<AtlasPlacement> Placements { get; init; }
}

public static class AtlasPacker
{
    public static AtlasResult Build(IEnumerable<LibImage> images, int maxPageSize = 2048, string pagePrefix = "atlas")
    {
        List<LibImage> ordered = images
            .Where(static image => image.Width > 0 && image.Height > 0 && image.MainBgra.Length >= image.Width * image.Height * 4)
            .OrderByDescending(static image => image.Height)
            .ThenByDescending(static image => image.Width)
            .ThenBy(static image => image.Index)
            .ToList();

        if (ordered.Count == 0) return new AtlasResult { Pages = [], Placements = [] };

        int maxDimension = ordered.Max(static image => Math.Max(image.Width, image.Height));
        long area = ordered.Sum(static image => (long)(image.Width + 2) * (image.Height + 2));
        int estimated = NextPowerOfTwo((int)Math.Ceiling(Math.Sqrt(area)));
        int pageSize = Math.Clamp(Math.Max(64, Math.Max(NextPowerOfTwo(maxDimension), estimated)), 64, maxPageSize);
        if (pageSize > 4096)
            throw new InvalidDataException($"Image dimension {maxDimension} exceeds the maximum 4096 atlas size.");

        List<AtlasPageBuilder> pages = [];
        List<AtlasPlacement> placements = [];
        foreach (LibImage image in ordered)
        {
            AtlasPageBuilder? page = pages.LastOrDefault(value => value.TryPlace(image));
            if (page is null)
            {
                page = new AtlasPageBuilder(pages.Count, pageSize, pagePrefix);
                pages.Add(page);
                if (!page.TryPlace(image))
                    throw new InvalidDataException($"Image {image.Index} does not fit in a {pageSize} atlas.");
            }

            placements.Add(new AtlasPlacement
            {
                ImageIndex = image.Index,
                PageIndex = page.Index,
                PageFile = page.PageFile,
                X = page.LastX,
                Y = page.LastY,
                Width = image.Width,
                Height = image.Height,
            });
        }

        return new AtlasResult
        {
            Pages = pages.Select(static page => page.Build()).ToArray(),
            Placements = placements,
        };
    }

    private static int NextPowerOfTwo(int value)
    {
        int result = 1;
        while (result < value) result <<= 1;
        return result;
    }

    private sealed class AtlasPageBuilder
    {
        private readonly int _index;
        private readonly int _size;
        private readonly string _pagePrefix;
        private readonly byte[] _pixels;
        private int _cursorX;
        private int _cursorY;
        private int _shelfHeight;

        public AtlasPageBuilder(int index, int size, string pagePrefix)
        {
            _index = index;
            _size = size;
            _pagePrefix = pagePrefix;
            _pixels = new byte[size * size * 4];
        }

        public int Index => _index;
        public string PageFile => $"{_pagePrefix}-{_index}.png";
        public int LastX { get; private set; }
        public int LastY { get; private set; }

        public bool TryPlace(LibImage image)
        {
            const int padding = 2;
            if (image.Width + padding > _size || image.Height + padding > _size) return false;
            if (_cursorX + image.Width + padding > _size)
            {
                _cursorY += _shelfHeight + padding;
                _cursorX = 0;
                _shelfHeight = 0;
            }
            if (_cursorY + image.Height + padding > _size) return false;

            LastX = _cursorX;
            LastY = _cursorY;
            CopyBgra(_pixels, _size, image.MainBgra, image.Width, image.Height, LastX, LastY);
            _cursorX += image.Width + padding;
            _shelfHeight = Math.Max(_shelfHeight, image.Height);
            return true;
        }

        public AtlasPage Build() => new()
        {
            PageIndex = Index,
            PageFile = PageFile,
            Width = _size,
            Height = _size,
            Rgba = _pixels,
        };
    }

    private static void CopyBgra(byte[] target, int targetWidth, byte[] source, int width, int height, int x, int y)
    {
        for (int row = 0; row < height; row++)
        {
            int sourceOffset = row * width * 4;
            int targetOffset = ((y + row) * targetWidth + x) * 4;
            for (int column = 0; column < width; column++)
            {
                int s = sourceOffset + column * 4;
                int t = targetOffset + column * 4;
                target[t] = source[s + 2];
                target[t + 1] = source[s + 1];
                target[t + 2] = source[s];
                target[t + 3] = source[s + 3];
            }
        }
    }
}
