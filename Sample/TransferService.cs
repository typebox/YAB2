namespace Sample
{

    public class TransferService
    {
        public bool ValidateFunds(decimal amount, decimal balance)
        {
            if (amount <= 0) return false;
            
            if (amount > 1000)
            {
                // Large transfers have extra validation
                return balance >= (amount * 1.01m); // 1% buffer for large transfers
            }

            return balance >= amount;
        }
    }
}
