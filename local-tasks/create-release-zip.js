const
    gulp = requireModule("gulp-with-help"),
    editXml = require("gulp-edit-xml"),
    project = "WindowsServiceWatchdog",
    releasesFolder = "releases",
    fs = require("fs").promises,
    exists = require("fs").existsSync,
    name = project,
    releaseTarget = "net462",
    zipFolder = require("zip-folder");

function findVersionIn(propertyGroup) {
    return ["Version", "AssemblyVersion", "FileVersion"].reduce(
        (acc, cur) => {
            if (propertyGroup[cur] && propertyGroup[cur].length) {
                var result = (propertyGroup[cur][0] || "").trim();
                return result === "" ? undefined : result;
            }
            return acc;
        }, undefined);
}

gulp.task("create-release-zip", async () => {
    if (!exists(releasesFolder)) {
        await fs.mkdir(releasesFolder);
    }
    return await new Promise((resolve, reject) => {
        const projectFile = `src/${project}/${project}.csproj`;
        gulp.src(projectFile)
        .pipe(editXml(async (xml) => {
                const
                    version = xml.Project.PropertyGroup.reduce(
                        (acc, cur) => acc || findVersionIn(cur),
                        null);
                if (version === undefined) {
                    throw new Error(`no Version specified in any PropertyGroups for ${projectFile}`);
                }
                const
                    zipFile = `${releasesFolder}/${name}-${version}.zip`,
                    srcFolder = `src/${project}/bin/Release/${releaseTarget}`;
                if (exists(zipFile)) {
                    throw new Error(`release already exists at ${zipFile}`);
                }
                if (!exists(srcFolder)) {
                    throw new Error(`release source folder not found: ${srcFolder}`);
                }
                var files = await fs.readdir(srcFolder);
                if (files.length === 0) {
                    throw new Error(`no files found in ${srcFolder}`);
                }
                zipFolder(
                    srcFolder,
                    zipFile, err => {
                    if (err) {
                        return reject(err);
                    }
                    resolve();
                });
            })
        );
    });
});
