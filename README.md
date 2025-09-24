# TaskBatcher

This package collects incoming requests and processes them in batches.
It is particularly useful in scenarios where multiple requests can be consolidated into a single database call, significantly reducing overhead and improving application scalability.

## Usage/Examples

```c#
//Define result:
public struct Result { public int V { get; init; } }
//Define input that must be processed in batch:
public struct Input { public int V { get; init; } }

async Task Main()
{
	//Define your processing function (from here you can make bulk db call):
	var func = async (Dictionary<string, Input> inputs) =>
	{
		return inputs.ToDictionary(el => el.Key, el => new Result { V = el.Value.V * 2 });
	};
	//Create batcher instance and pass your func, worker count and batch size:
	var taskBatcher = new TaskBatcher<Result, Input>(func, 4, 7);
	//Run some tasks:
	List<Task<Result>> tasks = [];
	for (int i = 0; i < 127; i++) tasks.Add(taskBatcher.Run(Guid.NewGuid().ToString(), new Input { V = i }));
	await Task.WhenAll(tasks);
	//Print result:
	Console.WriteLine("RESULTS:");
	Console.WriteLine(string.Join(',', tasks.Select(el => el.Result.V).ToArray()));
}
```
