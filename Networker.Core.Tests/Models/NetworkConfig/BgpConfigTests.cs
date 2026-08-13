using System.ComponentModel.DataAnnotations;
using Networker.Core.Models.NetworkConfig;

namespace Networker.Core.Tests.Models.NetworkConfig;

public class BgpConfigTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void Validate_AcceptsSupportedAsNumbers(int localAs)
    {
        var config = new BgpConfig { LocalAs = localAs };

        config.Validate();
        Assert.Empty(ValidateAnnotations(config));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_RejectsNonPositiveAsNumbers(int localAs)
    {
        var config = new BgpConfig { LocalAs = localAs };

        Assert.Throws<ValidationException>(config.Validate);
        Assert.NotEmpty(ValidateAnnotations(config));
    }

    private static List<ValidationResult> ValidateAnnotations(BgpConfig config)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(config, new ValidationContext(config), results, validateAllProperties: true);
        return results;
    }
}
