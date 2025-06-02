using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Hubs
{
    public class ConnectionMappingService : IConnectionMappingService
    {
        private readonly ConcurrentDictionary<int, ConcurrentHashSet<string>> _connections =
            new ConcurrentDictionary<int, ConcurrentHashSet<string>>();

        // Stores Connection ID -> User ID (for faster lookup during disconnects)
        private readonly ConcurrentDictionary<string, int> _connectionToUserMap =
            new ConcurrentDictionary<string, int>();

        public void AddConnection(int userId, string connectionId)
        {
            var connectionsForUser = _connections.GetOrAdd(userId, _ => new ConcurrentHashSet<string>());
            connectionsForUser.Add(connectionId); 

            _connectionToUserMap.TryAdd(connectionId, userId); 

            Console.WriteLine($"Connection added: User {userId}, Connection {connectionId}. Total connections for user: {connectionsForUser.Count}");
        }

        public void RemoveConnection(int userId, string connectionId)
        {
            if (_connections.TryGetValue(userId, out var connections))
            {
                connections.Remove(connectionId); 
                if (connections.IsEmpty)
                {
                    // If the user has no more active connections, remove their entry
                    _connections.TryRemove(userId, out _);
                }
            }
            _connectionToUserMap.TryRemove(connectionId, out _); // Remove from reverse map

            Console.WriteLine($"Connection removed: User {userId}, Connection {connectionId}");
        }

        public IEnumerable<string> GetConnections(int userId)
        {
            if (_connections.TryGetValue(userId, out var connections))
            {
                return connections;
            }
            return Enumerable.Empty<string>();
        }

        public int GetUserId(string connectionId)
        {
            if (_connectionToUserMap.TryGetValue(connectionId, out int userId))
            {
                return userId;
            }
            return 0; // Return 0 or handle as an error if connectionId not found
        }
    }

    // --- ConcurrentHashSet Helper Class ---
    // (This is a simple thread-safe wrapper for a HashSet, needed because ConcurrentDictionary
    //  doesn't have a ConcurrentHashSet value type directly)
    public class ConcurrentHashSet<T>  : ICollection<T> where T : notnull
    {
        private readonly ConcurrentDictionary<T, byte> _dictionary = new ConcurrentDictionary<T, byte>();

        public bool Add(T item) => _dictionary.TryAdd(item, 0);

        public bool Remove(T item) => _dictionary.TryRemove(item, out _);

        public void Clear() => _dictionary.Clear();

        public bool Contains(T item) => _dictionary.ContainsKey(item);

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array == null) throw new System.ArgumentNullException(nameof(array));
            if (arrayIndex < 0) throw new System.ArgumentOutOfRangeException(nameof(arrayIndex));
            if (array.Length - arrayIndex < Count) throw new System.ArgumentException("Insufficient space in the target array.");

            foreach (var item in _dictionary.Keys)
            {
                array[arrayIndex++] = item;
            }
        }
        public int Count => _dictionary.Count;
        public bool IsReadOnly => false;

        void ICollection<T>.Add(T item) => Add(item);
        bool ICollection<T>.Remove(T item) => Remove(item);

        public IEnumerator<T> GetEnumerator() => _dictionary.Keys.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public bool IsEmpty => _dictionary.IsEmpty;
    }
}
