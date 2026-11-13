using NUnit.Framework;
using System;

namespace StreamDeckMonSwitch.Tests
{
    /**
     * <summary>
     * Runs before any fixture in this namespace. The test assembly references the
     * plugin executable, so merely touching its types runs static constructors in
     * the TEST HOST. Stretch the native monitor re-enumeration interval so the
     * MonitorConfigurator timer never fires inside the test host (child plugin
     * processes inherit this too, which is harmless for tests).
     * </summary>
     */
    [SetUpFixture]
    public class GlobalTestSetup
    {
        [OneTimeSetUp]
        public void SetUp()
        {
            Environment.SetEnvironmentVariable("SDMONSWITCH_UPDATE_PERIOD_MS", int.MaxValue.ToString());
        }
    }
}
