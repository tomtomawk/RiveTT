using RiveTT.Plugin.UI;
using Xunit;

namespace RiveTT.Tests;

public class StatusDialogContentTests
{
    [Theory]
    [InlineData(true, "Mode : Écriture autorisée", "modifications du modèle sont autorisées")]
    [InlineData(false, "Mode : Lecture seule", "modifications sont bloquées")]
    [InlineData(null, "Mode : Indisponible", "ne peut pas être déterminé")]
    public void ModeAlwaysIncludesTheActualPermission(bool? allowed, string heading, string explanation)
    {
        var content = StatusDialogContent.Create(allowed, true, "Phase1_Creation", "0.5.0", "2027");
        Assert.Equal(heading, content.Heading);
        Assert.Contains(explanation, content.Body);
        Assert.Contains("Document actif : Phase1_Creation", content.Body);
        Assert.Equal("RiveTT 0.5.0  •  Revit 2027", content.Footer);
    }

    [Fact]
    public void StoppedServiceDoesNotImplyThatTheWriteLockChanged()
    {
        var content = StatusDialogContent.Create(true, false, null, "0.5.0", null);
        Assert.Equal("Mode : Écriture autorisée", content.Heading);
        Assert.Contains("RiveTT est indisponible", content.Body);
        Assert.Contains("Aucun document ouvert", content.Body);
        Assert.Equal("RiveTT 0.5.0", content.Footer);
    }

    [Fact]
    public void ListeningServiceIsReady_NotProofOfAConnectedAssistant()
    {
        var content = StatusDialogContent.Create(false, true, " ", "0.5.0", "2026");
        Assert.Contains("prêt à recevoir", content.Body);
        Assert.DoesNotContain("connecté", content.Body);
        Assert.Contains("Aucun document ouvert", content.Body);
        Assert.DoesNotContain("Canal nommé", content.Body);
        Assert.DoesNotContain("origine", content.Body);
    }
}
