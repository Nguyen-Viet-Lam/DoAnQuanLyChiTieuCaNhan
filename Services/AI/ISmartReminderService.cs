using SmartSpendAI.Models.Dtos.Dashboard;

namespace SmartSpendAI.Services.AI
{
    public interface ISmartReminderService
    {
        Task<List<AiReminderResponse>> GetMonthlyRemindersAsync(int userId, CancellationToken cancellationToken);
    }
}
