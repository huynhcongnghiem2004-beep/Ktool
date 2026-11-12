KTool PaintPro - Revit 2024 / .NET Framework 4.8

THAY FILE
1. Sao lưu project và đóng Revit.
2. Thay PaintProEventHandler.cs, PaintProWindow.xaml, PaintProWindow.xaml.cs, PaintProCommand.cs.
3. App.cs được cung cấp để tham khảo. Nếu App.cs hiện tại đã có:
   btnPaintPro.LargeImage = LoadIcon("PaintPro_32.png");
   btnPaintPro.Image = LoadIcon("PaintPro_16.png");
   thì KHÔNG cần thay App.cs.
4. Chép hai file Resources/PaintPro_32.png và PaintPro_16.png vào thư mục
   Resources của project, đặt Build Action = Embedded Resource.
   Dim.csproj hiện có <EmbeddedResource Include="Resources\\*.png" />.
5. PaintProWindow.xaml: Build Action = Page. Đảm bảo chỉ có một class PaintProWindow.
6. Clean + Rebuild Solution. Khởi động lại Revit sau khi triển khai DLL.

SỬ DỤNG
- Chọn vật liệu > Paint liên tục > chọn lần lượt các mặt > Esc kết thúc.
- Mỗi mặt được commit bằng một Transaction riêng.
- Hút vật liệu vẫn chọn một mặt.

LƯU Ý
- Không build/kiểm thử trực tiếp trên máy có Revit 2024 tại đây.
- Phiên Paint liên tục thực hiện trong một lần Execute của ExternalEvent.
- Khi đang PickObject, cửa sổ WPF được ẩn; Esc mới hiển thị lại.
- Các mặt không cho phép Paint được bỏ qua và tính vào số skipped.
