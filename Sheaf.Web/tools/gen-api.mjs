import { spawnSync } from 'node:child_process'
import { accessSync, constants } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const repositoryRoot = resolve(webRoot, '..')
const apiProject = resolve(repositoryRoot, 'Sheaf', 'Sheaf.csproj')
const openApiDocument = resolve(repositoryRoot, 'Sheaf', 'openapi', 'v1.json')
const generatedTypes = resolve(webRoot, 'src', 'api', 'schema.d.ts')
const generatorCli = resolve(webRoot, 'node_modules', 'openapi-typescript', 'bin', 'cli.js')

function run(command, args, options) {
  const result = spawnSync(command, args, { stdio: 'inherit', ...options })

  if (result.error) {
    throw result.error
  }

  if (result.status !== 0) {
    process.exit(result.status ?? 1)
  }
}

const buildEnvironment = {
  ...process.env,
  'DOTNET_ENVIRONMENT': 'Production',
  'ASPNETCORE_ENVIRONMENT': 'Production',
}

// The exporter caches its file list, not the document; force a fresh export even if obj was removed.
run('dotnet', ['build', apiProject, '--configuration', 'Release', '--no-incremental'], {
  cwd: repositoryRoot,
  env: buildEnvironment,
})

accessSync(generatorCli, constants.R_OK)

run(process.execPath, [generatorCli, openApiDocument, '--output', generatedTypes], {
  cwd: webRoot,
})
