import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/connection/constants.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:share_plus/share_plus.dart';
import 'package:url_launcher/url_launcher.dart';

class WelcomePage extends StatelessWidget {
  const WelcomePage({super.key});

  static String get _shareText =>
      tr('Download KZ Companion for Windows: {url}', {'url': Constants.siteUrl});

  void _goToPairing(BuildContext context) {
    final nav = Navigator.of(context);
    if (nav.canPop()) {
      nav.pop();
    } else {
      nav.pushReplacementNamed('/config');
    }
  }

  Future<void> _openSite(BuildContext context) async {
    final ok = await launchUrl(Uri.parse(Constants.siteUrl),
        mode: LaunchMode.externalApplication);
    if (!ok && context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(tr('Could not open the browser.'))),
      );
    }
  }

  Future<void> _copyLink(BuildContext context) async {
    await Clipboard.setData(const ClipboardData(text: Constants.siteUrl));
    if (context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(tr('Link copied.'))),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final muted = theme.textTheme.bodyMedium?.copyWith(
      color: theme.colorScheme.onSurface.withValues(alpha: 0.7),
    );

    return Scaffold(
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.fromLTRB(24, 32, 24, 24),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Image.asset('assets/images/logo.png', height: 88),
              const SizedBox(height: 24),
              Text(
                tr('Welcome to KZ Companion'),
                textAlign: TextAlign.center,
                style: theme.textTheme.headlineSmall
                    ?.copyWith(fontWeight: FontWeight.bold),
              ),
              const SizedBox(height: 12),
              Text(
                tr('This phone becomes a button panel for your PC. To work, it needs the KZ Companion program running on the computer (Windows 10 or 11).'),
                textAlign: TextAlign.center,
                style: muted,
              ),
              const SizedBox(height: 28),
              _Step(
                number: 1,
                title: tr('On the PC, download KZ Companion'),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Text(tr('Open this address in the computer\'s browser:'),
                        style: muted),
                    const SizedBox(height: 10),
                    _SiteBox(onTap: () => _copyLink(context)),
                    const SizedBox(height: 10),
                    Row(
                      children: [
                        Expanded(
                          child: OutlinedButton.icon(
                            onPressed: () => SharePlus.instance
                                .share(ShareParams(text: _shareText)),
                            icon: const Icon(Icons.share, size: 18),
                            label: Text(tr('Send link')),
                          ),
                        ),
                        const SizedBox(width: 8),
                        Expanded(
                          child: OutlinedButton.icon(
                            onPressed: () => _copyLink(context),
                            icon: const Icon(Icons.copy, size: 18),
                            label: Text(tr('Copy')),
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 6),
                    Text(
                      tr('Tip: "Send link" sends it via WhatsApp or email so you can open it on the PC.'),
                      style: theme.textTheme.bodySmall,
                    ),
                  ],
                ),
              ),
              _Step(
                number: 2,
                title: tr('Install and open KZ Companion on the PC'),
                child: Text(
                  tr('Run KZCompanion-Setup.exe. If Windows warns that the app isn\'t recognized, click "More info" → "Run anyway". It doesn\'t open a window: it sits as an icon in the tray, near the clock. On Windows 11 the icon may be hidden under the ^ arrow next to it.'),
                  style: muted,
                ),
              ),
              _Step(
                number: 3,
                title: tr('Pair this phone'),
                child: Text(
                  tr('Right-click the KZ Companion icon → "Pair device (QR)" and scan the code here in the app. Phone and PC must be on the same Wi-Fi network (or connected by USB cable).'),
                  style: muted,
                ),
              ),
              const SizedBox(height: 12),
              FilledButton.icon(
                onPressed: () => _goToPairing(context),
                icon: const Icon(Icons.qr_code_scanner),
                label: Text(tr('Already installed on the PC: pair')),
                style: FilledButton.styleFrom(
                  padding: const EdgeInsets.symmetric(vertical: 14),
                  textStyle: const TextStyle(fontWeight: FontWeight.w600),
                ),
              ),
              const SizedBox(height: 4),
              TextButton(
                onPressed: () => _openSite(context),
                child: Text(tr('Open the site on this phone')),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _Step extends StatelessWidget {
  final int number;
  final String title;
  final Widget child;

  const _Step({required this.number, required this.title, required this.child});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.only(bottom: 20),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          CircleAvatar(
            radius: 14,
            backgroundColor: theme.colorScheme.primary,
            child: Text('$number',
                style: TextStyle(
                    color: theme.colorScheme.onPrimary,
                    fontWeight: FontWeight.bold)),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Padding(
                  padding: const EdgeInsets.only(top: 3, bottom: 6),
                  child: Text(title,
                      style: theme.textTheme.titleMedium
                          ?.copyWith(fontWeight: FontWeight.w600)),
                ),
                child,
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _SiteBox extends StatelessWidget {
  final VoidCallback onTap;

  const _SiteBox({required this.onTap});

  @override
  Widget build(BuildContext context) {
    return Material(
      color: Theme.of(context).colorScheme.primary.withValues(alpha: 0.12),
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(10),
        side: BorderSide(color: Theme.of(context).colorScheme.primary.withValues(alpha: 0.6)),
      ),
      child: InkWell(
        borderRadius: BorderRadius.circular(10),
        onTap: onTap,
        child: const Padding(
          padding: EdgeInsets.symmetric(vertical: 14, horizontal: 12),
          child: Text(
            Constants.siteDisplay,
            textAlign: TextAlign.center,
            style: TextStyle(
              fontSize: 20,
              fontWeight: FontWeight.bold,
              letterSpacing: 0.3,
            ),
          ),
        ),
      ),
    );
  }
}
