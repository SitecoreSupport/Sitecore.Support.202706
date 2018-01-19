using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Sitecore.Abstractions;
using Sitecore.ContentSearch;
using Sitecore.ContentSearch.Diagnostics;
using Sitecore.ContentSearch.Maintenance;
using Sitecore.Data;
using Sitecore.DependencyInjection;
using Sitecore.Events;
using Sitecore.Install.Events;
using Sitecore.Jobs;

namespace Sitecore.Support.ContentSearch.Events
{
  public class PackagingEventHandler : Sitecore.ContentSearch.Events.PackagingEventHandler
  {
    public PackagingEventHandler() : base(ServiceLocator.ServiceProvider.GetRequiredService<BaseFactory>())
    {
    }

    public PackagingEventHandler(BaseFactory factory) : base(factory)
    {
    }

    public new void OnPackageInstallItemsEndHandler(object sender, EventArgs e)
    {
      var sitecoreEventArgs = e as SitecoreEventArgs;
      if (sitecoreEventArgs?.Parameters == null || sitecoreEventArgs.Parameters.Length != 1) return;
      var parameter = sitecoreEventArgs.Parameters[0] as InstallationEventArgs;
      if (parameter?.ItemsToInstall == null) return;
      HandleInstalledItems(parameter.ItemsToInstall.ToList());
    }

    public new void OnPackageInstallItemsEndRemoteHandler(object sender, EventArgs args)
    {
      var installationRemoteEventArgs = args as InstallationRemoteEventArgs;
      if (installationRemoteEventArgs == null) return;
      var list = installationRemoteEventArgs.ItemsToInstall.ToList();
      if (list.Count == 0) return;
      HandleInstalledItems(list);
    }

    protected override void HandleInstalledItems(List<ItemUri> installedItems)
    {
      CrawlingLog.Log.Info($"Updating '{installedItems.Count}' items from installed items.");
      var groupings = FilterOutItems(installedItems).Select(Database.GetItem).Where(item => item != null).GroupBy(item => ContentSearchManager.GetContextIndexName(new SitecoreIndexableItem(item)));
      var jobList = new List<Job>();

      foreach (var source in groupings)
      {
        if (string.IsNullOrEmpty(source.Key)) continue;
        CrawlingLog.Log.Info($"[Index={source.Key}] Updating '{source.Count()}' items from installed items.");
        var job = IndexCustodian.ForcedIncrementalUpdate(ContentSearchManager.GetIndex(source.Key), source.Select(item => new SitecoreItemUniqueId(item.Uri)));
        jobList.Add(job);
      }

      var jobOptions = new JobOptions("CheckIndexJobs", "Indexing", "shell", this, "CheckIndexJobs", new object[] {jobList});
      JobManager.Start(jobOptions);
    }

    private static void CheckIndexJobs(List<Job> jobList)
    {
      Thread.Sleep(100);
      foreach (var job in jobList)
      {
        while (!job.IsDone)
        {
          Thread.Sleep(100);
        }
      }

      CrawlingLog.Log.Info("Items from installed items have been indexed.");
    }
  }
}