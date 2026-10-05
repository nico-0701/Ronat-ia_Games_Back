using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using RonatIa.Games.Application.Sessions;
using RonatIa.Games.Domain.Sessions;

namespace RonatIa.Games.Api.Realtime;

/// <summary>
/// Depois de uma escrita bem-sucedida em <c>SessionsController</c> (POST, PUT, PATCH, DELETE com resposta 2xx), avisa os
/// assinantes por tempo real. Fica na borda HTTP de propósito: a camada de aplicação não conhece SignalR. Só enfileira; quem
/// envia é o <see cref="SessionBroadcaster"/>, depois que a resposta já saiu.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NotifySessionChangedAttribute : ActionFilterAttribute
{
    /// <summary>Ações que mudam a lista de partidas do grupo (nova, começou, terminou, entrou/saiu alguém...).</summary>
    private static readonly HashSet<string> LifecycleActions =
        ["Create", "Join", "Leave", "AddPlayer", "RemovePlayer", "Start", "Cancel", "Finish", "Rematch"];

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();

        if (executed.Exception is not null || HttpMethods.IsGet(context.HttpContext.Request.Method))
        {
            return;
        }

        var value = (executed.Result as ObjectResult)?.Value;
        var status = executed.Result switch
        {
            ObjectResult { StatusCode: { } code } => code,
            StatusCodeResult plain => plain.StatusCode,
            _ => StatusCodes.Status200OK,
        };

        if (status is < 200 or >= 300)
        {
            return;
        }

        var view = value switch
        {
            GameSessionDto session => session,
            ActionResponse response => response.Session,
            _ => null,
        };

        // Na criação não há id na rota; na revanche a rota traz a partida antiga e a resposta, a nova.
        var routeSessionId = context.RouteData.Values.TryGetValue("sessionId", out var raw) && Guid.TryParse(raw?.ToString(), out var parsed)
            ? parsed
            : (Guid?)null;

        var action = (context.ActionDescriptor as Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor)?.ActionName ?? string.Empty;
        var sessionId = routeSessionId ?? view?.Id;
        if (sessionId is null)
        {
            return;
        }

        var lifecycle = LifecycleActions.Contains(action) || view?.Status == SessionStatus.Finished;
        var rematch = action == "Rematch" ? view?.Id : null;

        context.HttpContext.RequestServices.GetRequiredService<SessionBroadcastQueue>()
            .Publish(new SessionNotice(sessionId.Value, view?.GroupId, lifecycle, rematch));
    }
}
