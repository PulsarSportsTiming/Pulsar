using System.Threading.Tasks;
using PulsarUI.Models;

namespace PulsarUI.Interfaces
{
    public interface IDatabaseService
    {
        Task WriteQueueAsync(RaceEntry entry);
    }
}