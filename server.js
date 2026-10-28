const app = require('./app');
const { ready } = require('./data/db');

const port = Number(process.env.PORT) || 5221;

ready()
  .then(() => {
    app.listen(port, () => console.log(`Lakeside Medical is running at http://localhost:${port}`));
  })
  .catch((error) => {
    console.error('Database setup failed.', error);
    process.exit(1);
  });
