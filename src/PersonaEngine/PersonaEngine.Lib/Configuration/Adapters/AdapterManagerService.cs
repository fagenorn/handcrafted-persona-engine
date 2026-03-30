// using System.Collections.Concurrent;
// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Logging;
// using Microsoft.Extensions.Options;
// using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
// using PersonaEngine.Lib.UI.Common;
// using Silk.NET.OpenGL;
//
// namespace PersonaEngine.Lib.Configuration.Adapters;
//
// public class AdapterManagerService : IStartupTask, IAsyncDisposable
// {
//     private readonly ConcurrentDictionary<Guid, IInputAdapter> _activeAdapters = new();
//
//     private readonly ConcurrentDictionary<Guid, InputAdapterConfig> _activeAdapterSettings = new();
//
//     private readonly ILogger<AdapterManagerService> _logger;
//
//     private readonly IOptionsMonitor<AvatarAppConfig> _optionsMonitor;
//
//     private readonly IServiceProvider _serviceProvider;
//
//     private IDisposable? _optionsChangeListener;
//
//     private readonly CancellationTokenSource _stoppingCts;
//
//     public AdapterManagerService(
//         ILogger<AdapterManagerService> logger,
//         IOptionsMonitor<AvatarAppConfig> optionsMonitor,
//         IServiceProvider serviceProvider
//     )
//     {
//         _logger = logger;
//         _optionsMonitor = optionsMonitor;
//         _serviceProvider = serviceProvider;
//         _stoppingCts = new CancellationTokenSource();
//     }
//
//     public async ValueTask DisposeAsync()
//     {
//         _logger.LogInformation("Adapter Manager Service stopping.");
//
//         _optionsChangeListener?.Dispose();
//
//         try
//         {
//             await _stoppingCts.CancelAsync();
//         }
//         catch (ObjectDisposedException) { }
//
//         var stopTasks = _activeAdapters
//             .Values.Select(adapter =>
//             {
//                 return Task.Run(async () =>
//                 {
//                     _logger.LogInformation(
//                         "Stopping adapter {AdapterId} during service shutdown...",
//                         adapter.Id
//                     );
//                     try
//                     {
//                         using var timeoutCts = new CancellationTokenSource(
//                             TimeSpan.FromSeconds(45)
//                         );
//
//                         await adapter.StopAsync(timeoutCts.Token);
//                         _logger.LogInformation(
//                             "Adapter {AdapterId} stopped during service shutdown.",
//                             adapter.Id
//                         );
//                     }
//                     catch (OperationCanceledException)
//                     {
//                         _logger.LogWarning(
//                             "Stopping adapter {AdapterId} cancelled or timed out during service shutdown.",
//                             adapter.Id
//                         );
//                     }
//                     catch (Exception ex)
//                     {
//                         _logger.LogError(
//                             ex,
//                             "Error stopping adapter {AdapterId} during service shutdown.",
//                             adapter.Id
//                         );
//                     }
//                     finally
//                     {
//                         try
//                         {
//                             _logger.LogInformation(
//                                 "Disposing adapter {AdapterId} during service shutdown...",
//                                 adapter.Id
//                             );
//                             await adapter.DisposeAsync();
//                             _logger.LogInformation(
//                                 "Adapter {AdapterId} disposed during service shutdown.",
//                                 adapter.Id
//                             );
//                         }
//                         catch (Exception ex)
//                         {
//                             _logger.LogError(
//                                 ex,
//                                 "Error disposing adapter {AdapterId} during service shutdown.",
//                                 adapter.Id
//                             );
//                         }
//                     }
//                 });
//             })
//             .ToList();
//
//         try
//         {
//             await Task.WhenAll(stopTasks);
//             _logger.LogInformation("All active adapters have been processed during shutdown.");
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(
//                 ex,
//                 "Error occurred while waiting for adapters to stop/dispose during shutdown."
//             );
//         }
//
//         _activeAdapters.Clear();
//         _activeAdapterSettings.Clear();
//         _logger.LogInformation("Adapter Manager Service stopped.");
//     }
//
//     public void Execute(GL gl)
//     {
//         _logger.LogInformation("Adapter Manager Service starting.");
//
//         try
//         {
//             ProcessConfigurationChanges(_optionsMonitor.CurrentValue);
//         }
//         catch (Exception ex)
//         {
//             _logger.LogCritical(
//                 ex,
//                 "Failed to process initial adapter configuration. Service may not function correctly."
//             );
//         }
//
//         _optionsChangeListener = _optionsMonitor.OnChange(ProcessConfigurationChanges);
//
//         _logger.LogInformation("Adapter Manager Service started and monitoring configuration.");
//     }
//
//     private void ProcessConfigurationChanges(AvatarAppConfig newSettings)
//     {
//         _logger.LogInformation("Detected configuration change. Processing adapter updates...");
//
//         // Flatten the desired state from configuration into a dictionary for easy lookup
//         var desiredSettings = newSettings
//             .Conversations.SelectMany(c => c.Inputs)
//             .ToDictionary(s => s.Id);
//
//         var currentAdapterIds = _activeAdapters.Keys.ToHashSet();
//         var desiredAdapterIds = desiredSettings.Keys.ToHashSet();
//
//         // --- Determine Changes ---
//         var idsToRemove = currentAdapterIds.Except(desiredAdapterIds).ToList();
//         var idsToAdd = desiredAdapterIds.Except(currentAdapterIds).ToList();
//         var idsToCheckForUpdate = currentAdapterIds.Intersect(desiredAdapterIds).ToList();
//
//         // --- 1. Remove Adapters No Longer in Config ---
//         foreach (var idToRemove in idsToRemove)
//         {
//             StopAndRemoveAdapter(idToRemove);
//         }
//
//         // --- 2. Add New Adapters ---
//         foreach (var idToAdd in idsToAdd)
//         {
//             if (desiredSettings.TryGetValue(idToAdd, out var settingToAdd))
//             {
//                 CreateAndStartAdapter(settingToAdd);
//             }
//             else
//             {
//                 _logger.LogWarning(
//                     "Attempted to add adapter with ID '{AdapterId}' but setting was not found in desired state (concurrent update?). Skipping.",
//                     idToAdd
//                 );
//             }
//         }
//
//         // --- 3. Check Existing Adapters for Updates ---
//         // Simple strategy: If an adapter's config *might* have changed, recreate it.
//         // TODO More complex: Deep compare old/new settings (requires storing old setting).
//         foreach (var idToCheck in idsToCheckForUpdate)
//         {
//             if (
//                 desiredSettings.TryGetValue(idToCheck, out var newSetting)
//                 && _activeAdapterSettings.TryGetValue(idToCheck, out _)
//             )
//             {
//                 // Basic check: Are settings objects different?
//                 // For more robustness, serialize both to JSON and compare strings,
//                 // or implement IEquatable on setting types.
//                 // This simple reference check or basic property check often suffices if
//                 // IOptionsMonitor creates new setting objects on change.
//                 _logger.LogInformation(
//                     "Adapter {AdapterId} exists in new config. Recreating to apply potential changes.",
//                     idToCheck
//                 );
//
//                 StopAndRemoveAdapter(idToCheck); // Stop and remove the old one
//                 CreateAndStartAdapter(newSetting); // Create and start the new one
//             }
//             else
//             {
//                 _logger.LogWarning(
//                     "Checking adapter ID '{AdapterId}' for update, but its new or old setting could not be retrieved reliably. Skipping update check.",
//                     idToCheck
//                 );
//             }
//         }
//
//         _logger.LogInformation("Finished processing adapter configuration changes.");
//     }
//
//     private void CreateAndStartAdapter(InputAdapterConfig setting)
//     {
//         if (string.IsNullOrWhiteSpace(setting.Type))
//         {
//             _logger.LogError(
//                 "Attempted to create adapter with invalid setting (Missing Type). Setting: {@Setting}",
//                 setting
//             );
//
//             return;
//         }
//
//         if (_activeAdapters.ContainsKey(setting.Id))
//         {
//             _logger.LogWarning(
//                 "Attempted to create adapter {AdapterId}, but an instance with this ID already exists.",
//                 setting.Id
//             );
//
//             return;
//         }
//
//         _logger.LogInformation(
//             "Creating adapter {AdapterId} of type {AdapterType}...",
//             setting.Id,
//             setting.Type
//         );
//
//         IInputAdapter? newAdapter = null;
//         try
//         {
//             newAdapter = (IInputAdapter)
//                 ActivatorUtilities.CreateInstance(
//                     _serviceProvider,
//                     setting.AdapterInstanceType,
//                     setting
//                 );
//
//             newAdapter.Id = setting.Id;
//
//             if (!_activeAdapterSettings.TryAdd(setting.Id, setting))
//             {
//                 _logger.LogWarning(
//                     "Failed to store settings for adapter {AdapterId} after creation.",
//                     setting.Id
//                 );
//             }
//
//             if (_activeAdapters.TryAdd(newAdapter.Id, newAdapter))
//             {
//                 _logger.LogInformation("Adapter {AdapterId} created. Starting...", newAdapter.Id);
//             }
//             else
//             {
//                 // Adapter with this ID was added by another thread concurrently? Log and dispose the one we created.
//                 _logger.LogWarning(
//                     "Failed to add created adapter {AdapterId} to collection (already exists?). Disposing instance.",
//                     newAdapter.Id
//                 );
//
//                 _activeAdapterSettings.TryRemove(setting.Id, out _); // Clean up settings entry too
//                 _ = Task.Run(async () => await newAdapter.DisposeAsync());
//             }
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(
//                 ex,
//                 "Failed to create adapter instance for ID {AdapterId}, Type {AdapterType}. Check configuration and DI registrations.",
//                 setting.Id,
//                 setting.Type
//             );
//
//             if (newAdapter != null)
//             {
//                 _ = Task.Run(async () => await newAdapter.DisposeAsync());
//             }
//
//             _activeAdapterSettings.TryRemove(setting.Id, out _);
//         }
//     }
//
//     private void StopAndRemoveAdapter(Guid adapterId)
//     {
//         _logger.LogInformation("Attempting to stop and remove adapter {AdapterId}...", adapterId);
//         if (_activeAdapters.TryRemove(adapterId, out var adapterToStop))
//         {
//             _activeAdapterSettings.TryRemove(adapterId, out _);
//             _logger.LogInformation(
//                 "Adapter {AdapterId} removed from active collection. Stopping...",
//                 adapterId
//             );
//
//             _ = Task.Run(async () =>
//             {
//                 try
//                 {
//                     using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
//                     await adapterToStop.StopAsync(timeoutCts.Token);
//                     _logger.LogInformation("Adapter {AdapterId} stopped.", adapterId);
//                 }
//                 catch (OperationCanceledException)
//                 {
//                     _logger.LogWarning(
//                         "Stopping adapter {AdapterId} timed out or was cancelled.",
//                         adapterId
//                     );
//                 }
//                 catch (Exception ex)
//                 {
//                     _logger.LogError(ex, "Error stopping adapter {AdapterId}.", adapterId);
//                 }
//                 finally
//                 {
//                     try
//                     {
//                         await adapterToStop.DisposeAsync();
//                         _logger.LogInformation("Adapter {AdapterId} disposed.", adapterId);
//                     }
//                     catch (Exception ex)
//                     {
//                         _logger.LogError(ex, "Error disposing adapter {AdapterId}.", adapterId);
//                     }
//                 }
//             });
//         }
//         else
//         {
//             _logger.LogWarning(
//                 "Adapter {AdapterId} not found in active collection for removal.",
//                 adapterId
//             );
//             _activeAdapterSettings.TryRemove(adapterId, out _);
//         }
//     }
// }
