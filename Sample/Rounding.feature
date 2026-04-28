Feature: Money Rounding
    As a banking system
    I want to ensure all money transfers follow rounding policies
    So that balances remain consistent

    @yab-concept:Rounding
    Scenario: Transfer with precision rounding
        Given a transfer amount of 100.005
        And an account balance of 200.00
        When the transfer is validated
        Then the result should be true

    @yab-concept:Rounding
    Scenario: Large transfer requires buffer
        Given a transfer amount of 2000.00
        And an account balance of 2010.00
        When the transfer is validated
        Then the result should be false

    @yab-concept:Rounding
    Scenario: Invalid transfer amount
        Given a transfer amount of -10.00
        And an account balance of 100.00
        When the transfer is validated
        Then the result should be false
