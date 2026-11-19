using NUnit.Framework;
using Agent.Rpa.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class ThemeColorMathTests
    {
        [Test]
        public void IsLight_VsDarkToolWindowBackground_ReturnsFalse()
        {
            // Visual Studio dark theme tool-window background is #252526.
            Assert.That(ThemeColorMath.IsLight(0x25, 0x25, 0x26), Is.False);
        }

        [Test]
        public void IsLight_VsLightToolWindowBackground_ReturnsTrue()
        {
            // Visual Studio light theme tool-window background is #F5F5F5.
            Assert.That(ThemeColorMath.IsLight(0xF5, 0xF5, 0xF5), Is.True);
        }

        [Test]
        public void IsLight_Black_ReturnsFalse()
        {
            Assert.That(ThemeColorMath.IsLight(0, 0, 0), Is.False);
        }

        [Test]
        public void IsLight_White_ReturnsTrue()
        {
            Assert.That(ThemeColorMath.IsLight(255, 255, 255), Is.True);
        }

        [Test]
        public void IsLight_Threshold_BetweenDarkAndLight()
        {
            // Luma crosses 0.5 between 127 and 128 gray.
            Assert.That(ThemeColorMath.IsLight(127, 127, 127), Is.False);
            Assert.That(ThemeColorMath.IsLight(128, 128, 128), Is.True);
        }
    }
}
