using Xunit;
using Sample;
using Yab.Attributes;

namespace Sample.Tests
{
    [Concept("Rounding")]
    public class TransferServiceTests
    {
        [Fact]
        public void Should_Validate_Funds()
        {
            var service = new TransferService();
            var result = service.ValidateFunds(100, 200);
            Assert.True(result);
        }
    }
}


/*yab-docs
[yab-hash:TransferServiceTests:YwqOK6Pg79qb9s3U0ftBI3sEsIRhwMwcvhHNbeF6ZV4=]
*/