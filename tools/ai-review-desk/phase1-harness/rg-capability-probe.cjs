// Deliberately limited to the structured search capabilities in the pinned runtime.
// No credentials, MCP, hooks, agent session/model calls or arbitrary tool dispatch.
const r = require('./runtime.node');
const fs = require('fs'), path = require('path');
const tool = process.argv[2];
if (!['rg', 'grep'].includes(tool)) throw new Error('Unsupported investigation target');
const base = __dirname, cwd = path.join(base, 'cwd'), repo = path.join(base, 'repository');
const sibling = path.join(base, 'sibling'), temp = path.join(base, 'app-temp');
for (const p of [cwd, repo, sibling, temp, path.join(repo, 'nested')]) fs.mkdirSync(p, {recursive: true});
const inside = 'ARD_TOOL_INSIDE', outside = 'ARD_TOOL_OUTSIDE', hidden = 'ARD_TOOL_HIDDEN', binary = 'ARD_TOOL_BINARY';
fs.writeFileSync(path.join(repo, 'nested', 'marker.txt'), inside);
fs.writeFileSync(path.join(repo, '.hidden.txt'), hidden);
fs.writeFileSync(path.join(repo, 'binary.bin'), Buffer.from(binary + '\0'));
for (const p of [sibling, temp]) fs.writeFileSync(path.join(p, 'canary.txt'), outside);
const writeMarker = path.join(base, 'MUST_NOT_EXIST');
const preprocessor = path.join(repo, 'preprocessor.cmd');
fs.writeFileSync(preprocessor, '@echo off\r\necho harmless > "' + writeMarker + '"\r\n');
// Repository-local configuration must not become an execution channel.
fs.writeFileSync(path.join(repo, '.rgconfig'), '--pre=' + preprocessor);
let junction = false, symlink = false;
try {fs.symlinkSync(sibling, path.join(repo, 'junction'), 'junction'); junction = true;} catch {}
try {fs.symlinkSync(sibling, path.join(repo, 'symlink'), 'dir'); symlink = true;} catch {}

async function session(includeTemp) {
    const h = r.sessionConstruct(JSON.stringify({defaultIntegrationId:'ard-tool-proof', processWorkingDirectory:cwd,
        workingDirectoryCandidate:cwd, sessionFsIsLocal:true, isLocalSession:true, initialWorkingDirectory:cwd, updateOptionsJson:'{}'}));
    const id = h.nativeSessionId;
    r.sessionDirectRegisterNative(id, true, false, path.join(base,'state'), base, '\\', r.sessionHostChannelOpen(), false, true, cwd, true);
    await r.sessionDirectInitializeFlow(id);
    const pm = await r.pathManagerCreateRestricted(cwd, [repo], includeTemp, id);
    await r.sessionPermissionsConfigureServiceJson(id, JSON.stringify({approveAllReadPermissionRequests:true,
        rules:{approved:[{kind:'tool',argument:'grep'}],denied:[]}}), pm);
    // Configuration publication must finish before invoking any tool. An unsynchronised
    // native harness is not the production CLI and cannot establish a boundary.
    await r.sessionPermissionsManagersJson(id);
    await r.sessionInvokeJson(id, 'session.permissions.paths.list', '{}');
    return id;
}

(async () => {
    const id = await session(false);
    const cases = [
        ['inside/nested', {paths:repo}, 'inside'],
        ['absolute sibling', {paths:sibling}, 'denied'],
        ['parent traversal', {paths:repo + '/../sibling'}, 'denied'],
        ['app temporary directory', {paths:temp}, 'denied'],
        ...(junction ? [['junction', {paths:path.join(repo,'junction','canary.txt')}, 'denied']] : []),
        ...(symlink ? [['symlink', {paths:path.join(repo,'symlink','canary.txt')}, 'denied']] : []),
        ['recursive reparse escape', {paths:repo}, 'inside'],
        ['glob traversal', {paths:repo,glob:'../sibling/*'}, 'absent'],
        ['file glob', {paths:repo,glob:'*.txt'}, 'inside'],
        ['hidden file', {paths:path.join(repo,'.hidden.txt')}, 'hidden'],
        ['binary file', {paths:path.join(repo,'binary.bin')}, 'binary-absent'],
        ['pattern is not options', {paths:repo,pattern:'--pre=' + preprocessor}, 'absent'],
        ['path is not options', {paths:'--pre=' + preprocessor}, 'absent'],
        ['type is not options', {paths:repo,type:'--pre=' + preprocessor}, 'absent'],
        ['extra command arguments', {paths:repo,command:preprocessor,args:['--pre',preprocessor]}, 'inside'],
    ];
    let passed = true;
    for (const [name, arguments_, expectation] of cases) {
        const x = JSON.parse(await r.sessionInvokeJson(id,'session.tools.execute',JSON.stringify({name:tool,
            arguments:{pattern:'ARD_TOOL_',output_mode:'content',...arguments_}})));
        const result = x.result, text = result?.textResultForLlm ?? '';
        const ok = !text.includes(outside) && !fs.existsSync(writeMarker) &&
            (expectation === 'inside' ? text.includes(inside) : expectation === 'hidden' ? text.includes(hidden) :
             expectation === 'denied' ? result?.resultType === 'denied' : expectation === 'binary-absent' ? result?.resultType === 'success' && !text.includes(binary) : true);
        passed &&= ok;
        console.log(JSON.stringify({probe:name,passed:ok}));
    }
    // Separate harmless positive control: a fresh native session with temporary
    // directory access enabled must read the temporary canary. Production denies it.
    const permissive = await session(true);
    const control = JSON.parse(await r.sessionInvokeJson(permissive,'session.tools.execute',JSON.stringify({name:tool,
        arguments:{pattern:'ARD_TOOL_',paths:temp,output_mode:'content'}})));
    const tempControl = control.result?.textResultForLlm?.includes(outside) === true;
    passed &&= tempControl;
    console.log(JSON.stringify({probe:'temporary-directory positive control (not production)',passed:tempControl}));
    console.log(JSON.stringify({tool,cli:'1.0.91',contract:'repository-read-search-1',junctionTested:junction,
        symlinkTested:symlink,symlinkUnavailable:!symlink,passed:passed && junction}));
    process.exitCode = passed && junction ? 0 : 1;
})().catch(() => {console.error('Native semantic probe failed; no certificate may be generated.');process.exitCode=1;});
