/**
 * A photograph from somebody's phone, made small enough to put on a website.
 *
 * **The browser does this, not the server.** A picture straight out of a phone camera is four thousand pixels
 * wide and several megabytes, and a static export has no image optimizer behind it — `next.config.ts` turns
 * Next's off, because optimizing needs a running server and a published Webly site is a directory of files. So
 * whatever is uploaded is exactly what every visitor downloads, and an unshrunk holiday snap on a contact page
 * is a slow site on a phone in a shop with one bar of signal.
 *
 * Doing it here rather than in C# is what keeps the backend free of an image library: resizing needs a real
 * decoder for every format, and the one thing every customer already has is a browser that decoded the picture
 * to show it to them. It also means the megabytes never cross the network at all.
 *
 * It is a convenience, not a guarantee — `UploadSiteImages` re-checks the size and the format, because a
 * request can be made without this code ever running.
 */

/** The widest a site image is allowed to be. Two thousand covers a full-width hero on a large screen. */
const MAX_EDGE = 2000;

/** JPEG quality for the re-encode. 0.82 is where the artefacts stop being visible on a photograph. */
const QUALITY = 0.82;

/** Below this, re-encoding is more likely to make the file bigger than smaller, so it is left alone. */
const LEAVE_ALONE_BYTES = 400 * 1024;

/**
 * Shrinks an image if it is worth shrinking, and hands back the original otherwise.
 *
 * Never throws: a file this cannot decode — an unusual format, a corrupt one, a browser without the canvas —
 * is passed through untouched, and the server decides what to do with it. Failing the upload here would mean
 * a photograph somebody can see on their own screen being refused with a message about a canvas.
 */
export async function shrinkImage(file: File): Promise<File> {
  if (typeof document === 'undefined') return file;
  if (!file.type.startsWith('image/')) return file;

  // GIFs can be animated and a canvas keeps one frame, which would silently turn somebody's animation into a
  // still. PNGs of logos and screenshots are left alone below the threshold for the same reason: a re-encode
  // to JPEG would put grey fringes on transparent edges.
  if (file.type === 'image/gif') return file;

  try {
    const bitmap = await createImageBitmap(file);
    const scale = Math.min(1, MAX_EDGE / Math.max(bitmap.width, bitmap.height));

    if (scale === 1 && file.size <= LEAVE_ALONE_BYTES) {
      bitmap.close();
      return file;
    }

    const canvas = document.createElement('canvas');
    canvas.width = Math.round(bitmap.width * scale);
    canvas.height = Math.round(bitmap.height * scale);

    const context = canvas.getContext('2d');
    if (!context) return file;

    context.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
    bitmap.close();

    const blob = await new Promise<Blob | null>(resolve => canvas.toBlob(resolve, 'image/jpeg', QUALITY));

    // A re-encode that came out bigger is not an improvement, and a transparent PNG that came out as a JPEG
    // with a black background would be worse than either.
    if (!blob || (blob.size >= file.size && scale === 1)) return file;

    return new File([blob], renamed(file.name), { type: 'image/jpeg', lastModified: file.lastModified });
  } catch {
    return file;
  }
}

/** The same name with a .jpg on it, because the bytes are now a JPEG and the extension is what serves it. */
function renamed(name: string): string {
  const stem = name.replace(/\.[^.]+$/, '');

  return `${stem || 'image'}.jpg`;
}
