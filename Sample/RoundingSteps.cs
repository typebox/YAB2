using Reqnroll;
using Xunit;
using Yab.Attributes;

namespace Sample.Steps
{
    [Binding]
    [Concept("Rounding")]
    public class RoundingSteps
    {
        private decimal _amount;
        private decimal _balance;
        private bool _result;
        private readonly TransferService _service = new TransferService();

        [Given("a transfer amount of (.*)")]
        public void GivenATransferAmountOf(decimal amount)
        {
            _amount = amount;
        }

        [Given("an account balance of (.*)")]
        public void GivenAnAccountBalanceOf(decimal balance)
        {
            _balance = balance;
        }

        [When("the transfer is validated")]
        public void WhenTheTransferIsValidated()
        {
            _result = _service.ValidateFunds(_amount, _balance);
        }

        [Then("the result should be (.*)")]
        public void ThenTheResultShouldBe(bool expected)
        {
            Assert.Equal(expected, _result);
        }
    }
}


/*yab-docs
    [yab-hash:RoundingSteps.ThenTheResultShouldBe:FtdHER+6jKijen+le710XcyBeq1r3rWr9u/vXUDWZRs=]
    [yab-hash:RoundingSteps.WhenTheTransferIsValidated:FSpYzjaojBGD81AzCd2HL5dKubEEGwq4KRpgWeojPO8=]
    [yab-hash:RoundingSteps.GivenAnAccountBalanceOf:EmWy/64HuarAtz+aqx1VAzBC3ibpLX0obROd2IJXZ3I=]
    [yab-hash:RoundingSteps.GivenATransferAmountOf:4I0DVGaIys/IjxFk/FnfG9uIBXp0/NA7XhdD/OiLd+4=]
[yab-hash:RoundingSteps:j3MkGvaD7a41rSpC2UJXoBP2fjceV0N6MyGhFVeakMU=]
*/