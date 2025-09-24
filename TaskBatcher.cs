using System.Threading.Channels;

namespace TaskBatcher;

public class TaskBatcher<TResult, TInput>
{
	private readonly struct Item
	{
		public required string Id { get; init; }
		public required TInput Input { get; init; }
		public required TaskCompletionSource<TResult> Tsc { get; init; }
	}

	private readonly Channel<Item> Ch;
	private readonly Func<Dictionary<string, TInput>, Task<Dictionary<string, TResult>>> Func;
	private readonly int workerCount;
	private readonly int batchSize;

	//main constructor:
	public TaskBatcher(Func<Dictionary<string, TInput>, Task<Dictionary<string, TResult>>> func, int? workerCount = null, int? batchSize = null)
	{
		this.workerCount = workerCount ?? 1;
		this.batchSize = batchSize ?? 2;
		this.Func = func;
		this.Ch = Channel.CreateUnbounded<Item>();
		this.Process().ConfigureAwait(false);
	}

	public async Task<TResult> Run(string id, TInput input)
	{
		var item = new Item
		{
			Id = id,
			Input = input,
			Tsc = new()
		};
		await this.Ch.Writer.WriteAsync(item);
		return await item.Tsc.Task;
	}

	private async Task ProcessBatch(Dictionary<string, Item> dItem)
	{
		Dictionary<string, TResult> results;
		try
		{
			//process batch:
			results = await this.Func(dItem.ToDictionary(el => el.Key, el => el.Value.Input));
		}
		catch (Exception ex)
		{
			results = [];
			//cancel unreturned batch requests:
			foreach (var (_, item) in dItem)
				item.Tsc.SetException(ex);
			dItem.Clear();
		}
		//notify results:
		foreach (var (id, result) in results)
		{
			dItem[id].Tsc.SetResult(result);
			dItem.Remove(id);
		}
		//cancel unreturned batch requests:
		foreach (var (_, item) in dItem)
			item.Tsc.SetCanceled();
	}

	private Dictionary<string, Item> Dequeue(Queue<Item> batch)
	{
		Dictionary<string, Item> dItem = [];
		while (batch.TryDequeue(out var item))
			dItem[item.Id] = item;
		return dItem;
	}

	private async Task ProcessAfter(int workerId, Queue<Item> batch, CancellationToken token)
	{
		if (token.IsCancellationRequested)
			return;
		await Task.Delay(100, token);
		if (batch.Count == 0)
			return;
		//process batch:
		var d = Dequeue(batch);
		// Console.WriteLine($"worker#:{workerId}, timeout, processing..{d.Count()}");
		_ = Task.Run(() => ProcessBatch(d));
	}

	private async Task Process()
	{
		var workers = Enumerable.Range(0, this.workerCount).Select(w => Task.Run(() => WorkerLoop(w)));
		await Task.WhenAll(workers);
	}

	private async Task WorkerLoop(int workerId)
	{
		Queue<Item> batch = [];
		CancellationTokenSource cts = new();
		_ = ProcessAfter(workerId, batch, cts.Token);
		await foreach (var item in this.Ch.Reader.ReadAllAsync())
		{
			batch.Enqueue(item);
			bool batchFull = batch.Count >= this.batchSize;
			if (batchFull)
			{
				//reset timer:
				cts.Cancel();
				cts = new();
				_ = ProcessAfter(workerId, batch, cts.Token);
				//process batch:
				var d = Dequeue(batch);
				// Console.WriteLine($"worker#:{workerId}, batchFull, processing..{d.Count()}");
				_ = Task.Run(() => ProcessBatch(d));
			}
		}
	}
}
