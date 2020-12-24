using CodeCake.Abstractions;
using System.Collections.Generic;
using static CodeCake.Build;

namespace CodeCake
{

    public partial class NPMSolution : ICIPublishWorkflow
    {
        private ArtifactType _artifactType;

        public ArtifactType ArtifactType
        {
            get
            {
                if( _artifactType == null ) _artifactType = new NPMArtifactType( _globalInfo, this );
                return _artifactType;
            }
        }

        public void Pack() => RunPack();
    }


    public partial class Build
    {
        /// <summary>
        /// Supports NPM packages.
        /// </summary>
        public class NPMArtifactType : ArtifactType
        {
            public NPMArtifactType( StandardGlobalInfo globalInfo, NPMSolution solution )
                : base( globalInfo, "NPM" )
            {
                Solution = solution;
            }

            public NPMSolution Solution { get; }

            protected override IEnumerable<ILocalArtifact> GetLocalArtifacts() => Solution.AllPublishedProjects;


            protected override IEnumerable<ArtifactFeed> GetRemoteFeeds()
            {if( GlobalInfo.BuildInfo.Version.PackageQuality <= CSemVer.PackageQuality.Stable ) yield return new AzureNPMFeed( this, "Signature-Code", "Default", null );
}

            protected override IEnumerable<ArtifactFeed> GetLocalFeeds()
            {
                return new ArtifactFeed[] {
                    new NPMLocalFeed( this, GlobalInfo.LocalFeedPath )
                };
            }
        }
    }
}
