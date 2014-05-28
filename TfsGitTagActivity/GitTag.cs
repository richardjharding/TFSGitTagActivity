using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TfsGitTagActivity
{
    using System.Activities;
    using System.ComponentModel;
    using System.IO;

    using LibGit2Sharp;

    using Microsoft.TeamFoundation;
    using Microsoft.TeamFoundation.Build.Client;
    using Microsoft.TeamFoundation.Build.Common;
    using Microsoft.TeamFoundation.Build.Workflow.Tracking;
    using Microsoft.TeamFoundation.Common;

    /// <summary>
    /// Custom build activity to tag git sources as part of a TFS build
    /// </summary>
    [BuildActivity(HostEnvironmentOption.All)]
    public class GitTag : CodeActivity
    {
        /// <summary>
        /// Gets or sets the tag name prefix. This will be combined with the build number supplied by TFS to create the tag name
        /// </summary>
        [Browsable(true)]
        [DefaultValue("")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public InArgument<string> TagNamePrefix { get; set; }

        /// <summary>
        /// Gets or sets the source folder. Used to get a reference on the git repository
        /// </summary>
        [Browsable(true)]
        [DefaultValue("src")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public InArgument<string> SourceFolder { get; set; }

        /// <summary>
        /// The execute.
        /// </summary>
        /// <param name="context">
        /// The context.
        /// </param>
        protected override void Execute(CodeActivityContext context)
        {
            // The following is copied from the TFS 2013 InitializeEnvironment custom action
            var strs = new Dictionary<string, string>();
            var extension = context.GetExtension<IBuildAgent>();
            var buildDetail = context.GetExtension<IBuildDetail>();
            strs.Add(BuildCommonUtil.BuildDefinitionIdVariable, LinkingUtilities.DecodeUri(buildDetail.BuildDefinitionUri.AbsoluteUri).ToolSpecificId);
            strs.Add(BuildCommonUtil.BuildDefinitionPathVariable, buildDetail.BuildDefinition.FullPath);
            strs.Add(BuildCommonUtil.BuildAgentIdVariable, LinkingUtilities.DecodeUri(extension.Uri.AbsoluteUri).ToolSpecificId);
            strs.Add(BuildCommonUtil.BuildAgentNameVariable, extension.Name);
            string fullPath = FileSpec.GetFullPath(BuildCommonUtil.ExpandEnvironmentVariables(extension.BuildDirectory, strs));

            var sourceFilesFolder = Path.Combine(fullPath, this.SourceFolder.Get(context));
            this.WriteToLog(context, string.Format("Path used to reference Git repository: {0}", sourceFilesFolder));

            // Assumes the build number is in a suitable format - eg .123
            var tagName = string.Format("{0}{1}", this.TagNamePrefix.Get(context), buildDetail.BuildNumber);
            this.WriteToLog(context, string.Format("Tag name: {0}", tagName));

            using (var repository = new Repository(sourceFilesFolder))
            {
                var tag = repository.ApplyTag(tagName);
                this.WriteToLog(context, string.Format("Tag applied: {0}", tag.CanonicalName));

                // assumes remote to push to is origin
                var remote = repository.Network.Remotes["origin"];
                this.WriteToLog(context, string.Format("Pushing tag to {0}", remote.Url));
                var refspec = string.Format("refs/tags/{0}:refs/tags/{0}", tagName);
                this.WriteToLog(context, string.Format("Refspec used to push: {0}", refspec));
                repository.Network.Push(
                    remote,
                    refspec,
                    new PushOptions
                        {
                            Credentials = new DefaultCredentials(),
                            OnPushStatusError = (error) => this.WriteToLog(context, string.Format("{0} - {1}", error.Message, error.Reference))
                        });
            }
        }

        /// <summary>
        /// Helper method to write to the TFS diagnostic log.
        /// </summary>
        /// <param name="context">
        /// The context.
        /// </param>
        /// <param name="message">
        /// The message.
        /// </param>
        private void WriteToLog(CodeActivityContext context, string message)
        {
            context.Track(new BuildInformationRecord<BuildMessage>
                              {
                                  Value = new BuildMessage
                                              {
                                                  Importance = BuildMessageImportance.High,
                                                  Message = message
                                              }
                              });
        }
    }
}
