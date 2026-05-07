using Karl.Cli;

var root = KarlCliCommandFactory.CreateRootCommand();
var result = root.Parse(args);
return await result.InvokeAsync();
