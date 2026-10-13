using EggIncognito.Services.EggSharpSender;

namespace EggIncognito.Models.Admin;

public sealed record EggSharpSendResponse(bool Enabled, ProtoSendOutcome Proto, string Data, DataPushOutcome? LastData);
