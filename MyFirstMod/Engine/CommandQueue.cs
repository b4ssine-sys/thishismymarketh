using System.Collections.Generic;
using System.Threading;

namespace MyFirstMod
{
    // WO-40: lock-free multi-producer / single-consumer queue. The UI thread (or
    // any thread) enqueues; only the simulation thread drains. Producers push onto
    // an immutable linked stack with a CAS; the consumer swaps the whole stack out
    // in one exchange and reverses it, so items come out in enqueue order. A drain
    // of an empty queue allocates nothing.
    public sealed class CommandQueue<T> where T : class
    {
        private sealed class Node
        {
            public T Item;
            public Node Next;
        }

        private Node _head;

        public void Enqueue(T item)
        {
            Node node = new Node();
            node.Item = item;
            Node seen;
            do
            {
                seen = _head;
                node.Next = seen;
            }
            while (Interlocked.CompareExchange(ref _head, node, seen) != seen);
        }

        public bool IsEmpty
        {
            get { return Interlocked.CompareExchange(ref _head, null, null) == null; }
        }

        // Consumer only. Appends every pending item to dest in FIFO order and
        // returns how many were appended.
        public int DrainTo(List<T> dest)
        {
            Node node = Interlocked.Exchange(ref _head, null);
            if (node == null) return 0;

            int start = dest.Count;
            int count = 0;
            for (; node != null; node = node.Next)
            {
                dest.Add(node.Item);
                count++;
            }
            dest.Reverse(start, count);
            return count;
        }
    }
}
