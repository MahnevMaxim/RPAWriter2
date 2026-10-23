using System.Collections.Generic;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class ReadPathPolicyTests
    {
        private static readonly string[] SolutionRoot = { @"C:\Solution" };

        [Test]
        public void IsAllowed_InsideRoot_ReturnsTrue()
        {
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\src\Program.cs", SolutionRoot), Is.True);
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution", SolutionRoot), Is.True);
        }

        [Test]
        public void IsAllowed_OutsideRoot_ReturnsFalse()
        {
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Windows\System32\cmd.exe", SolutionRoot), Is.False);
            Assert.That(ReadPathPolicy.IsAllowed(@"D:\Other\file.txt", SolutionRoot), Is.False);
        }

        [Test]
        public void IsAllowed_SiblingRoot_IsNotTreatedAsChild()
        {
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution2\Program.cs", SolutionRoot), Is.False);
        }

        [Test]
        public void IsAllowed_NoRoots_ReturnsFalse()
        {
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\a.cs", new string[0]), Is.False);
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\a.cs", null), Is.False);
        }

        [Test]
        public void IsAllowed_DeniedDirectories_ReturnsFalse()
        {
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\bin\Debug\app.dll", SolutionRoot), Is.False);
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\obj\a.cs", SolutionRoot), Is.False);
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\.git\config", SolutionRoot), Is.False);
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\node_modules\pkg\index.js", SolutionRoot), Is.False);
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\packages\p\a.cs", SolutionRoot), Is.False);
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\TestResults\a.trx", SolutionRoot), Is.False);
        }

        [Test]
        public void IsAllowed_SecretFileNames_ReturnsFalse()
        {
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\.env", SolutionRoot), Is.False);
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\appsettings.Production.json", SolutionRoot), Is.False);
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\cert.pem", SolutionRoot), Is.False);
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\key.key", SolutionRoot), Is.False);
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\cert.pfx", SolutionRoot), Is.False);
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\web.config", SolutionRoot), Is.False);
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\MyProject.csproj.user", SolutionRoot), Is.False);
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\my-credentials.json", SolutionRoot), Is.False);
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\.ssh\id_rsa", SolutionRoot), Is.False);
        }

        [Test]
        public void IsAllowed_AppsettingsVariants_ReturnsFalse()
        {
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\appsettings.json", SolutionRoot), Is.False);
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\appsettings.Development.json", SolutionRoot), Is.False);
        }

        [Test]
        public void IsAllowed_EnvAndSshKeyVariants_ReturnsFalse()
        {
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\.env.local", SolutionRoot), Is.False);
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\.env.production", SolutionRoot), Is.False);
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\.ssh\id_rsa.pub", SolutionRoot), Is.False);
        }

        [Test]
        public void IsAllowed_SecretsAndCredentialsPrefixes_ReturnsFalse()
        {
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\secrets.json", SolutionRoot), Is.False);
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\config\credentials.txt", SolutionRoot), Is.False);
        }

        [Test]
        public void IsAllowed_OrdinaryFiles_ReturnsTrue()
        {
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\app.config", SolutionRoot), Is.True);
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\README.md", SolutionRoot), Is.True);
            Assert.That(ReadPathPolicy.IsAllowed(@"C:\Solution\src\Robot.xaml", SolutionRoot), Is.True);
        }

        [Test]
        public void IsReadAllowed_ActMode_AllowsSecretsAndOutsideRoots()
        {
            Assert.That(ReadPathPolicy.IsReadAllowed(@"C:\Solution\.env", SolutionRoot, AgentMode.Act), Is.True);
            Assert.That(ReadPathPolicy.IsReadAllowed(@"C:\Windows\System32\cmd.exe", SolutionRoot, AgentMode.Act), Is.True);
            Assert.That(ReadPathPolicy.IsReadAllowed(@"C:\Solution\bin\Debug\app.dll", SolutionRoot, AgentMode.Act), Is.True);
        }

        [Test]
        public void IsReadAllowed_PlanMode_EnforcesPolicy()
        {
            Assert.That(ReadPathPolicy.IsReadAllowed(@"C:\Solution\src\a.cs", SolutionRoot, AgentMode.Plan), Is.True);
            Assert.That(ReadPathPolicy.IsReadAllowed(@"C:\Solution\.env", SolutionRoot, AgentMode.Plan), Is.False);
            Assert.That(ReadPathPolicy.IsReadAllowed(@"C:\Windows\System32\cmd.exe", SolutionRoot, AgentMode.Plan), Is.False);
        }

        [Test]
        public void IsReadAllowed_HelpMode_EnforcesPolicy()
        {
            Assert.That(ReadPathPolicy.IsReadAllowed(@"C:\Solution\.env", SolutionRoot, AgentMode.Help), Is.False);
        }
    }
}
