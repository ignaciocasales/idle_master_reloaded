namespace IdleMaster
{
    public class Statistics
    {
        private uint _sessionMinutesIdled = 0;
        private uint _sessionCardIdled = 0;
        private uint _remainingCards = 0;

        public uint getSessionMinutesIdled()
        {
            return _sessionMinutesIdled;
        }

        public uint getSessionCardIdled()
        {
            return _sessionCardIdled;
        }

        public uint getRemainingCards()
        {
            return _remainingCards;
        }

        public void setRemainingCards(uint remainingCards)
        {
            _remainingCards = remainingCards;
        }

        public void checkCardRemaining(uint actualCardRemaining)
        {
            if (actualCardRemaining < _remainingCards)
            {
                increaseCardIdled(_remainingCards - actualCardRemaining);
                _remainingCards = actualCardRemaining;
            }
            else if (actualCardRemaining > _remainingCards)
            {
                _remainingCards = actualCardRemaining;
            }
        }

        public void increaseCardIdled(uint number)
        {
            Properties.Settings.Default.totalCardIdled += number;
            Properties.Settings.Default.Save();
            _sessionCardIdled += number;
        }

        public void increaseMinutesIdled()
        {
            Properties.Settings.Default.totalMinutesIdled++;
            Properties.Settings.Default.Save();
            _sessionMinutesIdled++;
        }
    }
}