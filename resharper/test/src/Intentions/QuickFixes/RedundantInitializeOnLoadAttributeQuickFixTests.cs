﻿using JetBrains.ReSharper.FeaturesTestFramework.Intentions;
using JetBrains.ReSharper.Plugins.Unity.Feature.Services.QuickFixes;
using NUnit.Framework;

namespace JetBrains.ReSharper.Plugins.Unity.Tests.Intentions.QuickFixes
{
    [TestUnity]
    public class RedundantInitializeOnLoadAttributeQuickFixAvailabilityTests : QuickFixAvailabilityTestBase
    {
        protected override string RelativeTestDataPath=> @"Intentions\QuickFixes\RedundantInitializeOnLoadAttribute\Availability";

        [Test] public void Test01() { DoNamedTest(); }
        [Test] public void Test02() { DoNamedTest(); }
    }

    [TestUnity]
    public class RedundantInitializeOnLoadAttributeQuickFixRemoveTests : CSharpQuickFixTestBase<RedundantAttributeDeadCodeQuickFix>
    {
        protected override string RelativeTestDataPath=> @"Intentions\QuickFixes\RedundantInitializeOnLoadAttribute";

        [Test] public void Test01() { DoNamedTest(); }
        [Test] public void Test02() { DoNamedTest(); }
    }

    [TestUnity]
    public class RedundantInitializeOnLoadAttributeQuickFixCreateTests : CSharpQuickFixTestBase<CreateFromUsageFix>
    {
        protected override string RelativeTestDataPath=> @"Intentions\QuickFixes\RedundantInitializeOnLoadAttribute";

        [Test] public void Test03() { DoNamedTest(); }
        [Test] public void Test04() { DoNamedTest(); }
    }
}