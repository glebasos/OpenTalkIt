// Every path here is relative: itch.io serves the page from a per-upload
// subdirectory of its CDN.
import { dotnet } from './_framework/dotnet.js';
import * as opentalkit from './opentalkit.js';

// Avalonia adds its canvas to #out beside the splash; drop the splash then.
const out = document.getElementById('out');
new MutationObserver((_, observer) => {
    if (out.querySelector(':scope > :not(#splash)')) {
        document.getElementById('splash')?.remove();
        observer.disconnect();
    }
}).observe(out, { childList: true });

try {
    const runtime = await dotnet.withApplicationArgumentsFromQuery().create();
    runtime.setModuleImports('opentalkit', opentalkit);
    await runtime.runMain();
} catch (error) {
    const splash = document.getElementById('splash');
    if (splash) {
        splash.classList.add('error');
        splash.textContent = 'OpenTalkIt could not start: ' + (error?.message ?? error);
    }
    throw error;
}
