using GoodDns.DNS;

namespace GoodDns.Storage
{

    class Entry
    {
        public Packet packet;
        public uint timeToLive;

        public Entry(Packet packet)
        {
            this.packet = packet;
            //checks if the packet has any anwsers
            if (packet.answerCount > 0)
            {
                //get the time to live from the first anwser
                timeToLive = packet.answers[0].ttl + (uint)DateTimeOffset.Now.ToUnixTimeSeconds();
            }
        }
    }

    public class Cache
    {
        List<Entry> entries = new List<Entry>();

        void addRecord(Packet packet)
        {
            //check if the packet contains an answer and a question
            if(packet.answerCount > 1 && packet.questionCount > 1) {
                //add the packet to the cache
                Entry entry = new Entry(packet);
                entries.Add(entry);
            }
        }

        //kill all expired entries
        void killExpired()
        {
            //get the current time
            uint currentTime = (uint)DateTimeOffset.Now.ToUnixTimeSeconds();
            //loop through all entries
            foreach(Entry entry in entries) {
                //check if the entry has expired
                if(entry.timeToLive < currentTime) {
                    //remove the entry
                    entries.Remove(entry);
                }
            }
        }
    }
}