using System.Text.Json;

namespace Storage_Service.Tests.EndToEnd.Support;

// Gelesene Queue-Zähler, Supabase-Zeilen und Events für Vergleiche und Nachweisdateien.
// Das sind Test-Datenmodelle, keine zusätzlichen Tests oder Nachrichten-Produzenten.
internal sealed record QueueState(string Name, int Ready, int Unacked, int Consumers);
internal sealed record StoredRow(Guid Id, Guid RoomId, Guid SenderId, Guid ReceiverId, string Content, DateTimeOffset CreatedAt);
internal sealed record MessageEvent(Guid MessageId, Guid SenderId, Guid TargetId, string Ciphertext, DateTimeOffset Timestamp);
internal sealed record ObservedEvent(string Exchange, string RoutingKey, MessageEvent Message, JsonElement Body, string[] MessageType);
