namespace TaskFlow.Domain.Tasks;

// "TaskItemStatus" y no "TaskStatus" para no chocar con System.Threading.Tasks.TaskStatus.
public enum TaskItemStatus
{
    Todo,
    InProgress,
    InReview,
    Done,
}
