using System.Text;
using FluentAssertions;
using UnityStoryExtractor.Core.Decryptor;
using UnityStoryExtractor.Core.Models;
using Xunit;

namespace UnityStoryExtractor.Tests.Unit;

/// <summary>
/// 復号器のユニットテスト
/// </summary>
public class DecryptorTests
{
    private readonly DecryptorManager _manager;

    public DecryptorTests()
    {
        _manager = new DecryptorManager();
    }

    [Fact]
    public void XorDecryptor_ShouldDecryptCorrectly()
    {
        // Arrange
        var decryptor = new XorDecryptor();
        var originalText = "Hello, World!";
        var key = new byte[] { 0x42 }; // Simple XOR key
        
        var encrypted = Encoding.UTF8.GetBytes(originalText)
            .Select(b => (byte)(b ^ key[0]))
            .ToArray();

        // Act
        var decrypted = decryptor.Decrypt(encrypted, key);
        var result = Encoding.UTF8.GetString(decrypted);

        // Assert
        result.Should().Be(originalText);
    }

    [Fact]
    public void Base64Decryptor_ShouldDecryptCorrectly()
    {
        // Arrange
        var decryptor = new Base64Decryptor();
        var originalText = "Hello, World!";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(originalText));
        var data = Encoding.ASCII.GetBytes(base64);

        // Act
        var decrypted = decryptor.Decrypt(data);
        var result = Encoding.UTF8.GetString(decrypted);

        // Assert
        result.Should().Be(originalText);
    }

    [Fact]
    public void Base64Decryptor_Detect_ShouldRecognizeBase64()
    {
        // Arrange
        var decryptor = new Base64Decryptor();
        var originalText = "This is a longer text for base64 encoding test.";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(originalText));
        var data = Encoding.ASCII.GetBytes(base64);

        // Act
        var detection = decryptor.Detect(data);

        // Assert
        detection.IsEncrypted.Should().BeTrue();
        detection.Type.Should().Be(EncryptionType.Base64);
        detection.Confidence.Should().BeGreaterThan(0.7);
    }

    [Fact]
    public void Base64Decryptor_Detect_ShouldNotRecognizePlainText()
    {
        // Arrange
        var decryptor = new Base64Decryptor();
        var plainText = "This is plain text with special characters: !@#$%^&*()";
        var data = Encoding.UTF8.GetBytes(plainText);

        // Act
        var detection = decryptor.Detect(data);

        // Assert
        detection.IsEncrypted.Should().BeFalse();
    }

    [Fact]
    public void DecryptorManager_TryDecrypt_WithUnencryptedData_ShouldReturnFalse()
    {
        // Arrange
        var plainText = "This is just plain text that is not encrypted.";
        var data = Encoding.UTF8.GetBytes(plainText);

        // Act
        var result = _manager.TryDecrypt(data);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("暗号化されていない");
    }

    [Fact]
    public void DecryptorManager_DetectEncryption_WithBase64_ShouldDetect()
    {
        // Arrange
        var originalText = "This is a test message for encryption detection.";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(originalText));
        var data = Encoding.ASCII.GetBytes(base64);

        // Act
        var detection = _manager.DetectEncryption(data);

        // Assert
        detection.IsEncrypted.Should().BeTrue();
        detection.Type.Should().Be(EncryptionType.Base64);
    }

    [Fact]
    public void AesDecryptor_CanDecrypt_WithValidData_ShouldReturnTrue()
    {
        // Arrange
        var decryptor = new AesDecryptor();
        var validAesData = new byte[48]; // 16 bytes IV + 32 bytes data
        new Random(42).NextBytes(validAesData);

        // Act
        var canDecrypt = decryptor.CanDecrypt(validAesData);

        // Assert
        canDecrypt.Should().BeTrue();
    }

    [Fact]
    public void AesDecryptor_CanDecrypt_WithInvalidLength_ShouldReturnFalse()
    {
        // Arrange
        var decryptor = new AesDecryptor();
        var invalidData = new byte[17]; // Not a multiple of 16

        // Act
        var canDecrypt = decryptor.CanDecrypt(invalidData);

        // Assert
        canDecrypt.Should().BeFalse();
    }

    [Fact]
    public void AesDecryptor_Decrypt_WithoutKey_ShouldThrowException()
    {
        // Arrange
        var decryptor = new AesDecryptor();
        var data = new byte[48];

        // Act & Assert
        Assert.Throws<ArgumentException>(() => decryptor.Decrypt(data, null));
    }
}
