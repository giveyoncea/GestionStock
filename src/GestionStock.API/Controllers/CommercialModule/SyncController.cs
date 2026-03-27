using GestionStock.Application.DTOs;
using GestionStock.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GestionStock.API.Controllers;

[ApiController]
[Route("api/commercial/sync")]
[Authorize]
[Tags("Commercial Sync")]
public class SyncController : ControllerBase
{
    private readonly ICommercialOfflineSyncService _syncService;

    public SyncController(ICommercialOfflineSyncService syncService)
    {
        _syncService = syncService;
    }

    [HttpGet("bootstrap")]
    public async Task<ActionResult<CommercialOfflineBootstrapDto>> GetBootstrap([FromQuery] DateTime? sinceUtc, CancellationToken cancellationToken)
    {
        var payload = await _syncService.GetBootstrapAsync(sinceUtc, cancellationToken);
        return Ok(payload);
    }

    [HttpPost("push")]
    public async Task<ActionResult<CommercialOfflinePushResponseDto>> Push([FromBody] CommercialOfflinePushRequestDto request, CancellationToken cancellationToken)
    {
        if (request.Operations is null || request.Operations.Count == 0)
        {
            return BadRequest(ResultDto.Erreur("Aucune operation offline a synchroniser."));
        }

        var payload = await _syncService.PushAsync(request, cancellationToken);
        return Ok(payload);
    }
}
