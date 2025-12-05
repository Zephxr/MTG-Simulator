using System.Collections.Generic;

namespace MTGSimulator.Game
{
    public class Player
    {
        public string Name { get; set; }
        public List<Card> Hand { get; private set; }
        public List<Card> Library { get; private set; }
        public List<Card> Graveyard { get; private set; }

        public Player(string name)
        {
            Name = name;
            Hand = new List<Card>();
            Library = new List<Card>();
            Graveyard = new List<Card>();
        }
    }
}

