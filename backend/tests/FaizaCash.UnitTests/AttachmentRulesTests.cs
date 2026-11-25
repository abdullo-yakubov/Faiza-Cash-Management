using FaizaCash.Application;
using Xunit;

namespace FaizaCash.UnitTests;

public class AttachmentRulesTests
{
    [Fact]
    public void SignaturesAreDetectedByContentNotExtension()
    {
        Assert.Equal(FileSignatures.Jpeg, FileSignatures.Detect(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0 }));
        Assert.Equal(FileSignatures.Png, FileSignatures.Detect(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }));
        Assert.Equal(FileSignatures.WebP, FileSignatures.Detect("RIFF\0\0\0\0WEBPVP8 "u8.ToArray()));
        Assert.Equal(FileSignatures.Pdf, FileSignatures.Detect("%PDF-1.7\n"u8.ToArray()));
        Assert.Null(FileSignatures.Detect("MZ\x90\0"u8.ToArray()));          // exe
        Assert.Null(FileSignatures.Detect("RIFF\0\0\0\0WAVEfmt "u8.ToArray())); // RIFF, но не WebP
        Assert.Null(FileSignatures.Detect(ReadOnlySpan<byte>.Empty));
    }

    [Theory]
    [InlineData("C:\\fakepath\\..\\scan.exe", "application/pdf", "scan.pdf")]
    [InlineData("../../etc/passwd", "image/png", "passwd.png")]
    [InlineData("", "image/jpeg", "document.jpg")]
    [InlineData("чек \"№5\".jpg", "image/jpeg", "чек №5.jpg")]
    public void FileNameNeverCarriesPathOrForeignExtension(string input, string type, string expected) =>
        Assert.Equal(expected, FileSignatures.SafeFileName(input, type));

    [Theory]
    [InlineData("operation", "draft", false)]
    [InlineData("operation", "posted", true)]
    [InlineData("receipt", "handed_over", false)]
    [InlineData("receipt", "accepted", true)]
    [InlineData("advance", "reported", false)]
    [InlineData("advance", "closed", true)]
    [InlineData("unknown", "draft", true)]
    public void LockedOwnersRejectChanges(string ownerType, string status, bool locked) =>
        Assert.Equal(locked, AttachmentRules.IsLocked(ownerType, status));

    [Fact]
    public void ModifyMatrixFollowsSeparationOfDuties()
    {
        Assert.True(AttachmentRules.CanModify("advance", accountant: true, cashier: false, manager: false, isAuthor: false));
        Assert.True(AttachmentRules.CanModify("operation", false, cashier: true, false, isAuthor: true));
        Assert.False(AttachmentRules.CanModify("operation", false, cashier: true, false, isAuthor: false));
        Assert.True(AttachmentRules.CanModify("receipt", false, false, manager: true, false));
        Assert.False(AttachmentRules.CanModify("operation", false, false, manager: true, true));
        Assert.False(AttachmentRules.CanModify("operation", false, false, false, true)); // admin/auditor
    }
}
