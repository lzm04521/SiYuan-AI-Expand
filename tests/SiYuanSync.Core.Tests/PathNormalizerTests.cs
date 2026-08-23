using SiYuanSync.Core.Sync;

using Xunit;

namespace SiYuanSync.Core.Tests;

public class PathNormalizerTests
{
    [Theory]
    [InlineData("杰普特", "/杰普特")]
    [InlineData("/杰普特/", "/杰普特")]
    [InlineData(" /杰普特//子目录/ ", "/杰普特/子目录")]
    public void NormalizeParentPath_fixes_slash_variants(string raw, string expected)
        => Assert.Equal(expected, PathNormalizer.NormalizeParentPath(raw));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("///")]
    public void NormalizeParentPath_rejects_empty(string raw)
        => Assert.Throws<PathNormalizerException>(() => PathNormalizer.NormalizeParentPath(raw));

    [Fact]
    public void NormalizeParentPath_rejects_dotdot_segment()
        => Assert.Throws<PathNormalizerException>(() => PathNormalizer.NormalizeParentPath("/a/../b"));

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

    [Theory]
    [InlineData("report.html", "/JPT/report")]
    [InlineData("report.htm", "/JPT/report")]
    [InlineData("report.HTML", "/JPT/report")]
    [InlineData("sub/x.htm", "/JPT/sub/x")]
    public void Html_relpath_maps_to_hpath(string rel, string expected)
        => Assert.Equal(expected, PathNormalizer.RelPathToHPath("/JPT", rel));
}
