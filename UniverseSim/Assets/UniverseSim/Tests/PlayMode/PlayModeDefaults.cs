using NUnit.Framework;
using UnityEngine;

namespace UniverseSim.Unity.Tests.PlayMode
{
    /// <summary>
    /// Settings for every PlayMode test (ADR-0019): the scene starts from the system view, as the tests were written
    /// for, instead of near the Earth in the ship; the pointer does not steer the ship (in the Editor the mouse is
    /// wherever it is); the logbook does not check its goals, so no toast changes the HUD between two observations;
    /// no sound plays; and the logbook keeps its progress under a key of its own, cleared before and after. A test that
    /// needs one of these turns it on and restores it.
    /// </summary>
    [SetUpFixture]
    public sealed class PlayModeDefaults
    {
        public const string LogbookKey = "usim.logbook.tests";

        private string storageKey;

        [OneTimeSetUp]
        public void Apply()
        {
            StartExperience.Enabled = false;
            ShipPilot.MouseSteering = false;
            Logbook.Checking = false;
            SoundScape.Silent = true;
            storageKey = Logbook.StorageKey;
            Logbook.StorageKey = LogbookKey;
            PlayerPrefs.DeleteKey(LogbookKey);
        }

        [OneTimeTearDown]
        public void Restore()
        {
            StartExperience.Enabled = true;
            ShipPilot.MouseSteering = true;
            Logbook.Checking = true;
            SoundScape.Silent = false;
            Logbook.StorageKey = storageKey;
            PlayerPrefs.DeleteKey(LogbookKey);
        }
    }
}
