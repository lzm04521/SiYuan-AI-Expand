using SiYuanSync.Core.Sync;

using Xunit;

namespace SiYuanSync.Core.Tests;

public class PathNormalizerTests
{
    [Fact]
    public void RelPath_maps_to_hpath()
        => Assert.Equal("/JPT/feat-login/方案",
            PathNormalizer.RelPathToHPath("/JPT", "feat-login/方案.md"));

    [Fact]
    public void Backslash_normalized()
        => Assert.Equal("/JPT/sub/x",
            PathNormalizer.RelPathToHPath("/JPT", @"sub\x.md"));

    [Fact]
    public void ParentPath_trailing_slash_normalized()
        => Assert.Equal("/JPT/x",
            PathNormalizer.RelPathToHPath("/JPT/", "x.md"));

    [Theory]
    [InlineData("../escape.md")]
    [InlineData("a/../../b.md")]
    public void Dotdot_segment_rejected(string rel)
        => Assert.Throws<PathNormalizerException>(() => PathNormalizer.RelPathToHPath("/JPT", rel));

    [Fact]
    public void Control_char_segment_rejected()
        => Assert.Throws<PathNormalizerException>(() =>
            PathNormalizer.RelPathToHPath("/JPT", "badname.md"));

    [Fact]
    public void Dot_segment_rejected()
        => Assert.Throws<PathNormalizerException>(() =>
            PathNormalizer.RelPathToHPath("/JPT", "./x.md"));
}
