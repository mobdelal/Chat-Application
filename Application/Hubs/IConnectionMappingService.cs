using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Hubs
{
    public interface IConnectionMappingService
    {
        void AddConnection(int userId, string connectionId);
        void RemoveConnection(int userId, string connectionId);
        IEnumerable<string> GetConnections(int userId);
        int GetUserId(string connectionId); 
    }
}
