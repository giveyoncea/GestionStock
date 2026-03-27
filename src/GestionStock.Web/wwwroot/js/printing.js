export function printDocument(title, html) {
    var printWindow = window.open("", "_blank", "width=1100,height=900");
    if (!printWindow) {
        return;
    }

    printWindow.document.open();
    printWindow.document.write(html);
    printWindow.document.close();
    printWindow.focus();

    window.setTimeout(function () {
        printWindow.print();
    }, 350);
}
