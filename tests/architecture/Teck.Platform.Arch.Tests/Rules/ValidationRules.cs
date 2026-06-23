using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using FluentValidation;

namespace Teck.Platform.Arch.Tests.Rules;

public static class ValidationRules
{
    public static void ValidatorsShouldBeSealed(Architecture architecture)
    {
        var rule = ArchRuleDefinition
            .Classes()
            .That()
            .ImplementInterface(typeof(IValidator<>))
            .Should()
            .BeSealed()
            .Because("validators should be sealed to prevent inheritance");

        rule.Check(architecture);
    }

    public static void ValidatorsShouldResideInValidationNamespace(Architecture architecture, string validationNamespace)
    {
        var rule = ArchRuleDefinition
            .Classes()
            .That()
            .ImplementInterface(typeof(IValidator<>))
            .Should()
            .ResideInNamespaceMatching(validationNamespace)
            .Because("validators should be organized in validation folders");

        rule.Check(architecture);
    }
}
