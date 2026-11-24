namespace Codepad.Application.Commands;

/// <summary>A command that matches a search, with its availability at the time of the search.</summary>
/// <param name="Command">The command.</param>
/// <param name="Availability">Whether the command can run now.</param>
/// <param name="Score">The match score. A higher score is a better match.</param>
public sealed record CommandMatch(AppCommand Command, CommandAvailability Availability, int Score);
