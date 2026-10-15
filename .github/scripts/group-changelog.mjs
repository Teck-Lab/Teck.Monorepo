import rendererModule from "nx/release/changelog-renderer";

const NativeRenderer = rendererModule.default ?? rendererModule;

export class ComponentChangelogRenderer extends NativeRenderer {
  // The platform section already identifies this component and its version.
  renderVersionTitle() {
    return "";
  }
}

// Use Nx's public renderer extension to aggregate fixed groups once, retaining
// native affected-project filtering, Conventional Commit sections and links.
export function groupChangelogRenderer(projects) {
  return class GroupChangelogRenderer extends ComponentChangelogRenderer {
    constructor(config) {
      super({
        ...config,
        project: null,
        changes: config.changes.filter(
          (change) =>
            change.affectedProjects === "*" ||
            change.affectedProjects?.some((project) => projects.includes(project)),
        ),
        dependencyBumps: config.dependencyBumps?.filter(
          (bump) => !projects.includes(bump.dependencyName),
        ),
      });
    }
  };
}
