using NUnit.Framework;

namespace Unity.Entities.Editor.Tests
{
    class LiveLinkConfigHelperTests
    {
        [Test]
        public void EnsreLiveLinkConfigHelperIsProperlyInitialized()
        {
            Assert.That(LiveLinkConfigHelper.IsProperlyInitialized, Is.True);
        }
    }
}
