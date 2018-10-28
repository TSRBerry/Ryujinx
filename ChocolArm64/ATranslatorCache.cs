using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace ChocolArm64
{
    class ATranslatorCache
    {
        //Maximum size of the cache, in bytes, measured in ARM code size.
        private const int MaxTotalSize = 4 * 1024 * 256;

        //Minimum time required in milliseconds for a method to be eligible for deletion.
        private const int MinTimeDelta = 2 * 60000;

        //Minimum number of calls required to update the timestamp.
        private const int MinCallCountForUpdate = 250;

        private class CacheBucket
        {
            public ATranslatedSub Subroutine { get; private set; }

            public LinkedListNode<long> Node { get; private set; }

            public int CallCount { get; set; }

            public int Size { get; private set; }

            public long Timestamp { get; private set; }

            public CacheBucket(ATranslatedSub Subroutine, LinkedListNode<long> Node, int Size)
            {
                this.Subroutine = Subroutine;
                this.Size       = Size;

                UpdateNode(Node);
            }

            public void UpdateNode(LinkedListNode<long> Node)
            {
                this.Node = Node;

                Timestamp = GetTimestamp();
            }
        }

        private ConcurrentDictionary<long, CacheBucket> Cache;

        private LinkedList<long> SortedCache;

        private int TotalSize;

        public ATranslatorCache()
        {
            Cache = new ConcurrentDictionary<long, CacheBucket>();

            SortedCache = new LinkedList<long>();
        }

        public void AddOrUpdate(long Position, ATranslatedSub Subroutine, int Size)
        {
            ClearCacheIfNeeded();

            TotalSize += Size;

            lock (SortedCache)
            {
                LinkedListNode<long> Node = SortedCache.AddLast(Position);

                CacheBucket NewBucket = new CacheBucket(Subroutine, Node, Size);

                Cache.AddOrUpdate(Position, NewBucket, (Key, Bucket) =>
                {
                    TotalSize -= Bucket.Size;

                    SortedCache.Remove(Bucket.Node);

                    return NewBucket;
                });
            }
        }

        public bool HasSubroutine(long Position)
        {
            return Cache.ContainsKey(Position);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetSubroutine(long Position, out ATranslatedSub Subroutine)
        {
            if (Cache.TryGetValue(Position, out CacheBucket Bucket))
            {
                if (Bucket.CallCount++ > MinCallCountForUpdate)
                {
                    if (Monitor.TryEnter(SortedCache))
                    {
                        try
                        {
                            Bucket.CallCount = 0;

                            SortedCache.Remove(Bucket.Node);

                            Bucket.UpdateNode(SortedCache.AddLast(Position));
                        }
                        finally
                        {
                            Monitor.Exit(SortedCache);
                        }
                    }
                }

                Subroutine = Bucket.Subroutine;

                return true;
            }

            Subroutine = default(ATranslatedSub);

            return false;
        }

        private void ClearCacheIfNeeded()
        {
            long Timestamp = GetTimestamp();

            while (TotalSize > MaxTotalSize)
            {
                lock (SortedCache)
                {
                    LinkedListNode<long> Node = SortedCache.First;

                    if (Node == null)
                    {
                        break;
                    }

                    CacheBucket Bucket = Cache[Node.Value];

                    long TimeDelta = Bucket.Timestamp - Timestamp;

                    if (TimeDelta <= MinTimeDelta)
                    {
                        break;
                    }

                    if (Cache.TryRemove(Node.Value, out Bucket))
                    {
                        TotalSize -= Bucket.Size;

                        SortedCache.Remove(Bucket.Node);
                    }
                }
            }
        }

        private static long GetTimestamp()
        {
            long timestamp = Stopwatch.GetTimestamp();

            return timestamp / (Stopwatch.Frequency / 1000);
        }
    }
}